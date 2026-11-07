using Microsoft.Extensions.Configuration;
using Timeglass.Storage.Settings;

namespace Timeglass.App.Settings;

/// <summary>
/// The configuration that <see cref="Core.Settings.TimeglassSettings"/> bind to. Kept apart from
/// the host configuration so that only the settings file's keys end up in the settings, and the
/// file can live in the user's profile rather than next to the exe.
/// </summary>
internal sealed class SettingsConfiguration : IDisposable
{
    public SettingsConfiguration(IConfiguration configuration, string? path = null)
    {
        Configuration = configuration;
        Path = path;
    }

    public IConfiguration Configuration { get; }

    /// <summary>The settings file, or <see langword="null"/> when the settings don't come from one (tests).</summary>
    public string? Path { get; }

    /// <summary>
    /// Opens <paramref name="path"/> (writing the defaults first if it doesn't exist, so there is
    /// a file to edit) and watches it, so edits apply without a restart.
    /// </summary>
    public static SettingsConfiguration Open(string path)
    {
        var fullPath = System.IO.Path.GetFullPath(path);
        SettingsFile.EnsureExists(fullPath);

        var configuration = new ConfigurationBuilder()
            .AddJsonFile(fullPath, optional: true, reloadOnChange: true)
            .Build();
        return new SettingsConfiguration(configuration, fullPath);
    }

    /// <summary>Stops watching the file.</summary>
    public void Dispose() => (Configuration as IDisposable)?.Dispose();
}
