using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Timeglass.Api;

/// <summary>What <see cref="ApiSecurity"/> checks requests against.</summary>
/// <param name="Port">The port the API listens on; the only one a <c>Host</c> header may name.</param>
/// <param name="Token">This run's access token.</param>
public sealed record ApiSecurityOptions(int Port, ApiAccessToken Token);

/// <summary>
/// Keeps the API to the user's own browser and tools (#29). Window titles are sensitive: no other
/// machine and no other website may read them.
/// </summary>
/// <remarks>
/// <para>Every request, in this order:</para>
/// <list type="number">
/// <item><c>Host</c> must be <c>127.0.0.1:port</c> or <c>localhost:port</c>, else 400. A website
/// that points its own name at 127.0.0.1 (DNS rebinding) sends its name and is turned away.</item>
/// <item>An <c>Origin</c> header must be this API's own origin, and <c>Sec-Fetch-Site</c>, when
/// sent, must be <c>same-origin</c> or <c>none</c>; else 403. There is no CORS, so a browser never
/// lets another page read an answer anyway; this also stops other pages from sending requests.</item>
/// <item>Everything except <see cref="SignInPath"/> needs the token, as the
/// <see cref="CookieName"/> cookie or an <c>Authorization: Bearer</c> header, else 401.</item>
/// </list>
/// <para>
/// The tray app opens <c>/auth?token=…</c> in the browser; that sets the cookie (<c>HttpOnly</c>,
/// <c>SameSite=Strict</c>) and redirects to the dashboard, so the token is never in a page's
/// JavaScript. Scripts read the token from <see cref="ApiRuntimeFile"/> and send it as a header.
/// </para>
/// </remarks>
public static class ApiSecurity
{
    /// <summary>The cookie that carries the token for the browser.</summary>
    public const string CookieName = "timeglass_token";

    /// <summary>Exchanges <c>?token=</c> for the cookie and redirects to <c>/</c>.</summary>
    public const string SignInPath = "/auth";

    /// <summary>Registers <paramref name="options"/> for <see cref="UseTimeglassApiSecurity"/>.</summary>
    public static IServiceCollection AddTimeglassApiSecurity(this IServiceCollection services, ApiSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);
        services.AddSingleton(options);
        return services;
    }

    /// <summary>Adds the checks to the pipeline. Call it before mapping any endpoint.</summary>
    public static IApplicationBuilder UseTimeglassApiSecurity(this IApplicationBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);
        var options = app.ApplicationServices.GetRequiredService<ApiSecurityOptions>();
        return app.Use((context, next) => CheckAsync(context, next, options));
    }

    /// <summary>Maps <see cref="SignInPath"/>, the one endpoint that needs no token.</summary>
    public static IEndpointRouteBuilder MapTimeglassSignIn(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapGet(SignInPath, SignIn).ExcludeFromDescription();
        return endpoints;
    }

    /// <summary>The address that signs the browser in and opens the dashboard.</summary>
    public static Uri SignInUrl(ApiSecurityOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new Uri($"http://127.0.0.1:{options.Port}{SignInPath}?token={Uri.EscapeDataString(options.Token.Value)}");
    }

    private static Task CheckAsync(HttpContext context, RequestDelegate next, ApiSecurityOptions options)
    {
        var request = context.Request;
        if (!IsAllowedHost(request.Host, options.Port))
        {
            return RejectAsync(context, StatusCodes.Status400BadRequest, "Invalid Host header.");
        }

        if (!IsSameOrigin(request, options.Port))
        {
            return RejectAsync(context, StatusCodes.Status403Forbidden, "Requests from other sites are not allowed.");
        }

        if (!request.Path.Equals(SignInPath, StringComparison.OrdinalIgnoreCase) && !HasToken(request, options.Token))
        {
            return RejectAsync(context, StatusCodes.Status401Unauthorized, "Open the dashboard from the Timeglass tray icon.");
        }

        return next(context);
    }

    private static IResult SignIn(string? token, HttpContext context, ApiSecurityOptions options)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers["Referrer-Policy"] = "no-referrer";
        if (!options.Token.Matches(token))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status401Unauthorized,
                detail: "The link is not valid (any more). Open the dashboard from the Timeglass tray icon.");
        }

        context.Response.Cookies.Append(CookieName, options.Token.Value, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Strict,
            Path = "/",
            IsEssential = true,
        });
        return TypedResults.Redirect("/");
    }

    private static bool IsAllowedHost(HostString host, int port) =>
        host.Port == port && IsLoopbackName(host.Host);

    private static bool IsLoopbackName(string name) =>
        name.Equals("127.0.0.1", StringComparison.Ordinal) || name.Equals("localhost", StringComparison.OrdinalIgnoreCase);

    private static bool IsSameOrigin(HttpRequest request, int port)
    {
        var fetchSite = request.Headers["Sec-Fetch-Site"];
        if (fetchSite.Count > 0 && fetchSite != "same-origin" && fetchSite != "none")
        {
            return false;
        }

        var origin = request.Headers.Origin;
        if (origin.Count == 0)
        {
            return true;
        }

        return origin.Count == 1
            && Uri.TryCreate(origin[0], UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttp
            && uri.Port == port
            && uri.AbsolutePath == "/"
            && IsLoopbackName(uri.Host);
    }

    private static bool HasToken(HttpRequest request, ApiAccessToken token)
    {
        if (request.Cookies.TryGetValue(CookieName, out var cookie) && token.Matches(cookie))
        {
            return true;
        }

        var authorization = request.Headers.Authorization.ToString();
        const string Bearer = "Bearer ";
        return authorization.StartsWith(Bearer, StringComparison.OrdinalIgnoreCase)
            && token.Matches(authorization[Bearer.Length..].Trim());
    }

    private static Task RejectAsync(HttpContext context, int statusCode, string detail)
    {
        context.Response.Headers.CacheControl = "no-store";
        return TypedResults.Problem(statusCode: statusCode, detail: detail).ExecuteAsync(context);
    }
}
