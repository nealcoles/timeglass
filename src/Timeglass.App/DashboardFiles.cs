using Microsoft.Extensions.FileProviders;

namespace Timeglass.App;

/// <summary>The dashboard embedded in this assembly at build time (see <c>Dashboard.targets</c>, #30).</summary>
internal static class DashboardFiles
{
    /// <summary>The folder in the embedded files manifest that holds <c>web/dist</c>.</summary>
    public const string Root = "wwwroot";

    // Written by GenerateEmbeddedFilesManifest; ManifestEmbeddedFileProvider reads it.
    private const string ManifestName = "Microsoft.Extensions.FileProviders.Embedded.Manifest.xml";

    /// <summary>
    /// The embedded <c>web/dist</c>, or <see langword="null"/> in a build without it (a Debug build
    /// before the frontend was built).
    /// </summary>
    public static IFileProvider? Load()
    {
        var assembly = typeof(DashboardFiles).Assembly;
        if (assembly.GetManifestResourceInfo(ManifestName) is null)
        {
            return null;
        }

        var files = new ManifestEmbeddedFileProvider(assembly, Root);
        return files.GetFileInfo(Api.TimeglassDashboard.IndexFile).Exists ? files : null;
    }
}
