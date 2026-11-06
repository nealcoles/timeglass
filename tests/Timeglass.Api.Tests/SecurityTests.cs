using System.Net;
using System.Net.Http.Headers;
using static Timeglass.Api.Tests.ApiHost;

namespace Timeglass.Api.Tests;

/// <summary>The checks of <see cref="ApiSecurity"/> (#29), on the in-memory server.</summary>
public sealed class SecurityTests : IAsyncLifetime
{
    private const int Port = 5381;

    private readonly ApiAccessToken _token = ApiAccessToken.Create();
    private ApiHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        _host = await StartAsync(Utc("2026-11-05T09:00Z"), [], new ApiSecurityOptions(Port, _token), Ct);

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    private Task<HttpResponseMessage> SendAsync(string url, Action<HttpRequestMessage>? configure = null, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, new Uri(url, UriKind.Relative));
        configure?.Invoke(request);
        return _host.Client.SendAsync(request, Ct);
    }

    private void WithToken(HttpRequestMessage request) =>
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token.Value);

    [Theory]
    [InlineData("127.0.0.1:5381")]
    [InlineData("localhost:5381")]
    [InlineData("LOCALHOST:5381")]
    public async Task Loopback_host_names_are_accepted(string host)
    {
        using var response = await SendAsync("/api/status", r =>
        {
            WithToken(r);
            r.Headers.Host = host;
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("evil.example:5381")]
    [InlineData("evil.example")]
    [InlineData("127.0.0.1")]
    [InlineData("127.0.0.1:80")]
    [InlineData("localhost:5382")]
    [InlineData("192.168.1.20:5381")]
    [InlineData("[::1]:5381")]
    public async Task Other_host_headers_are_rejected(string host)
    {
        using var response = await SendAsync("/api/status", r =>
        {
            WithToken(r);
            r.Headers.Host = host;
        });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task A_wrong_host_is_rejected_before_sign_in()
    {
        using var response = await SendAsync($"/auth?token={_token.Value}", r => r.Headers.Host = "evil.example:5381");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
    }

    [Theory]
    [InlineData("/api/status")]
    [InlineData("/api/days/2026-11-05/summary")]
    [InlineData("/openapi/v1.json")]
    [InlineData("/")]
    public async Task Requests_without_a_token_get_401(string url)
    {
        using var response = await SendAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task A_wrong_bearer_token_gets_401()
    {
        using var response = await SendAsync("/api/status", r =>
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ApiAccessToken.Create().Value));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task A_wrong_cookie_gets_401()
    {
        using var response = await SendAsync("/api/status", r =>
            r.Headers.Add("Cookie", $"{ApiSecurity.CookieName}={ApiAccessToken.Create().Value}"));

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task The_bearer_token_lets_scripts_in()
    {
        using var response = await SendAsync("/api/status", WithToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Sign_in_sets_a_strict_http_only_cookie_and_redirects_to_the_dashboard()
    {
        using var response = await SendAsync(ApiSecurity.SignInUrl(_host.Security!).PathAndQuery, r => r.Headers.Add("Sec-Fetch-Site", "none"));

        response.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        response.Headers.Location.ShouldBe(new Uri("/", UriKind.Relative));
        response.Headers.CacheControl?.NoStore.ShouldBeTrue();
        var cookie = response.Headers.GetValues("Set-Cookie").ShouldHaveSingleItem();
        cookie.ShouldStartWith($"{ApiSecurity.CookieName}={_token.Value};");
        cookie.ShouldContain("httponly", Case.Insensitive);
        cookie.ShouldContain("samesite=strict", Case.Insensitive);
        cookie.ShouldContain("path=/", Case.Insensitive);

        using var api = await SendAsync("/api/status", r =>
        {
            r.Headers.Add("Cookie", cookie.Split(';')[0]);
            r.Headers.Add("Sec-Fetch-Site", "same-origin");
            r.Headers.Add("Origin", $"http://127.0.0.1:{Port}");
        });
        api.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?token=")]
    [InlineData("?token=nope")]
    public async Task Sign_in_with_a_wrong_token_gets_401_and_no_cookie(string query)
    {
        using var response = await SendAsync("/auth" + query);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        response.Headers.Contains("Set-Cookie").ShouldBeFalse();
    }

    [Theory]
    [InlineData("https://evil.example")]
    [InlineData("http://evil.example:5381")]
    [InlineData("http://127.0.0.1:5173")]
    [InlineData("https://127.0.0.1:5381")]
    [InlineData("null")]
    public async Task Requests_from_another_origin_are_rejected_even_with_the_token(string origin)
    {
        using var response = await SendAsync("/api/status", r =>
        {
            WithToken(r);
            r.Headers.Add("Origin", origin);
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Headers.Contains("Access-Control-Allow-Origin").ShouldBeFalse();
    }

    [Theory]
    [InlineData("cross-site")]
    [InlineData("same-site")]
    public async Task Requests_another_page_makes_are_rejected(string fetchSite)
    {
        using var response = await SendAsync("/api/status", r =>
        {
            WithToken(r);
            r.Headers.Add("Sec-Fetch-Site", fetchSite);
        });

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData("http://127.0.0.1:5381")]
    [InlineData("http://localhost:5381")]
    public async Task The_api_s_own_origin_is_allowed(string origin)
    {
        using var response = await SendAsync("/api/status", r =>
        {
            WithToken(r);
            r.Headers.Add("Origin", origin);
        });

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_cors_preflight_gets_no_cors_headers()
    {
        using var response = await SendAsync("/api/status", r =>
        {
            r.Headers.Add("Origin", "https://evil.example");
            r.Headers.Add("Access-Control-Request-Method", "GET");
            r.Headers.Add("Access-Control-Request-Headers", "authorization");
        }, HttpMethod.Options);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        response.Headers.Any(h => h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase)).ShouldBeFalse();
    }

    [Fact]
    public async Task The_sign_in_endpoint_is_not_in_the_openapi_document()
    {
        using var response = await SendAsync(TimeglassApi.OpenApiPath, WithToken);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain(ApiSecurity.SignInPath + "\"");
    }

    [Fact]
    public void Sign_in_url_points_at_loopback_with_the_token()
    {
        var url = ApiSecurity.SignInUrl(new ApiSecurityOptions(5400, ApiAccessToken.FromValue("abc-_123")));

        url.ShouldBe(new Uri("http://127.0.0.1:5400/auth?token=abc-_123"));
    }
}
