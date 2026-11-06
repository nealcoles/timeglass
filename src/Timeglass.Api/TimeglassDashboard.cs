using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;

namespace Timeglass.Api;

/// <summary>
/// Serves the built dashboard (<c>web/dist</c>) from the same server as the API (#30), so the
/// tray app is the only thing to install and run.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Files are served as they are. Those under <see cref="AssetsPath"/> have a content hash
/// in their name (Vite's default), so the browser may keep them for good; everything else,
/// <c>index.html</c> above all, is revalidated on every load so a new version shows at once.</item>
/// <item>Any other <c>GET</c> that doesn't look like a file gets <c>index.html</c>, so a deep
/// link such as <c>/days/2026-11-05</c> opens the dashboard on that page.</item>
/// <item>Paths under <c>/api</c> and <c>/openapi</c> never get the page: an unknown one is a 404,
/// so a typo in a script's URL fails instead of returning HTML.</item>
/// </list>
/// Call it after <see cref="ApiSecurity.UseTimeglassApiSecurity"/>: the dashboard needs the token
/// like the API does.
/// </remarks>
public static class TimeglassDashboard
{
    /// <summary>The page every dashboard route returns.</summary>
    public const string IndexFile = "index.html";

    /// <summary>Where Vite puts the hashed bundles.</summary>
    public const string AssetsPath = "/assets";

    /// <summary>For files whose name changes with their content.</summary>
    public const string ImmutableCacheControl = "public, max-age=31536000, immutable";

    /// <summary>For everything else: the browser may keep it but must ask first.</summary>
    public const string RevalidateCacheControl = "no-cache";

    private static readonly string[] ApiPrefixes = ["/api", "/openapi"];

    /// <summary>Serves <paramref name="files"/>, the contents of <c>web/dist</c>, as the dashboard.</summary>
    public static WebApplication MapTimeglassDashboard(this WebApplication app, IFileProvider files)
    {
        ArgumentNullException.ThrowIfNull(app);
        ArgumentNullException.ThrowIfNull(files);

        app.UseStaticFiles(new StaticFileOptions
        {
            FileProvider = files,
            ContentTypeProvider = new FileExtensionContentTypeProvider(),
            OnPrepareResponse = context => context.Context.Response.Headers.CacheControl =
                context.Context.Request.Path.StartsWithSegments(AssetsPath, StringComparison.OrdinalIgnoreCase)
                    ? ImmutableCacheControl
                    : RevalidateCacheControl,
        });

        // The default fallback pattern only matches paths without a file extension, so a missing
        // script is a 404 instead of a page of HTML the browser can't run.
        app.MapFallback(context => ServeIndexAsync(context, files))
            .WithMetadata(new HttpMethodMetadata([HttpMethods.Get, HttpMethods.Head]))
            .ExcludeFromDescription();

        return app;
    }

    private static Task ServeIndexAsync(HttpContext context, IFileProvider files)
    {
        var path = context.Request.Path;
        var index = files.GetFileInfo(IndexFile);
        if (ApiPrefixes.Any(prefix => path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)) || !index.Exists)
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status404NotFound).ExecuteAsync(context);
        }

        var response = context.Response;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers.CacheControl = RevalidateCacheControl;
        response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        if (HttpMethods.IsHead(context.Request.Method))
        {
            response.ContentLength = index.Length;
            return Task.CompletedTask;
        }

        return response.SendFileAsync(index, context.RequestAborted);
    }
}
