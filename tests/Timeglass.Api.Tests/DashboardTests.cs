using System.Net;
using System.Net.Http.Headers;
using static Timeglass.Api.Tests.ApiHost;

namespace Timeglass.Api.Tests;

/// <summary>The dashboard served next to the API (#30), behind the same checks.</summary>
public sealed class DashboardTests : IAsyncLifetime
{
    private const int Port = 5381;
    private const string Index = "<!doctype html><title>Timeglass</title><script type=\"module\" src=\"/assets/index-B4xq1.js\"></script>";

    private static readonly Dictionary<string, string> Files = new(StringComparer.Ordinal)
    {
        ["index.html"] = Index,
        ["assets/index-B4xq1.js"] = "console.log('timeglass');",
        ["assets/index-Cz9k2.css"] = "body{margin:0}",
        ["favicon.svg"] = "<svg xmlns=\"http://www.w3.org/2000/svg\"/>",
    };

    private readonly ApiAccessToken _token = ApiAccessToken.Create();
    private ApiHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() =>
        _host = await StartAsync(Utc("2026-11-05T09:00Z"), [], new ApiSecurityOptions(Port, _token), Files, Ct);

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    private Task<HttpResponseMessage> GetAsync(string url, bool withToken = true, HttpMethod? method = null)
    {
        var request = new HttpRequestMessage(method ?? HttpMethod.Get, new Uri(url, UriKind.Relative));
        if (withToken)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token.Value);
        }

        return _host.Client.SendAsync(request, Ct);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/index.html")]
    [InlineData("/days/2026-11-05")]
    [InlineData("/week/2026-W45/apps/outlook")]
    public async Task Pages_get_the_index_and_must_revalidate(string url)
    {
        using var response = await GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        response.Headers.CacheControl?.NoCache.ShouldBeTrue();
        response.Headers.CacheControl?.MaxAge.ShouldBeNull();
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe(Index);
    }

    [Theory]
    [InlineData("/assets/index-B4xq1.js", "text/javascript", "console.log('timeglass');")]
    [InlineData("/assets/index-Cz9k2.css", "text/css", "body{margin:0}")]
    public async Task Hashed_assets_are_immutable(string url, string mediaType, string content)
    {
        using var response = await GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe(mediaType);
        response.Headers.CacheControl?.ToString().ShouldBe(TimeglassDashboard.ImmutableCacheControl);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBe(content);
    }

    [Fact]
    public async Task Other_files_must_revalidate()
    {
        using var response = await GetAsync("/favicon.svg");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("image/svg+xml");
        response.Headers.CacheControl?.NoCache.ShouldBeTrue();
    }

    [Theory]
    [InlineData("/assets/index-gone.js")]
    [InlineData("/missing.png")]
    public async Task A_missing_file_is_404_not_the_page(string url)
    {
        using var response = await GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldNotBe("text/html");
    }

    [Fact]
    public async Task The_api_is_not_swallowed_by_the_fallback()
    {
        using var response = await GetAsync("/api/status");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/json");
    }

    [Theory]
    [InlineData("/api")]
    [InlineData("/api/nope")]
    [InlineData("/API/days")]
    [InlineData("/openapi/v2")]
    public async Task Unknown_api_paths_are_404_not_the_page(string url)
    {
        using var response = await GetAsync(url);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
    }

    [Fact]
    public async Task The_fallback_is_not_in_the_openapi_document()
    {
        using var response = await GetAsync(TimeglassApi.OpenApiPath);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("{path");
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/days/2026-11-05")]
    [InlineData("/assets/index-B4xq1.js")]
    public async Task The_dashboard_needs_the_token_too(string url)
    {
        using var response = await GetAsync(url, withToken: false);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync(Ct)).ShouldNotContain("Timeglass</title>");
    }

    [Fact]
    public async Task Head_gets_the_headers_without_the_page()
    {
        using var response = await GetAsync("/days/2026-11-05", method: HttpMethod.Head);

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("text/html");
        (await response.Content.ReadAsStringAsync(Ct)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Signing_in_opens_the_dashboard()
    {
        using var signIn = await GetAsync(ApiSecurity.SignInUrl(_host.Security!).PathAndQuery, withToken: false);
        signIn.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var cookie = signIn.Headers.GetValues("Set-Cookie").ShouldHaveSingleItem().Split(';')[0];

        using var request = new HttpRequestMessage(HttpMethod.Get, signIn.Headers.Location);
        request.Headers.Add("Cookie", cookie);
        request.Headers.Add("Sec-Fetch-Site", "same-origin");
        using var page = await _host.Client.SendAsync(request, Ct);

        page.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await page.Content.ReadAsStringAsync(Ct)).ShouldBe(Index);
    }
}
