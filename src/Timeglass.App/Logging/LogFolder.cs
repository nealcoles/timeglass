using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Timeglass.App.Logging;

/// <summary>The folder with the log files, and the tray's <i>Open log folder</i>.</summary>
internal sealed partial class LogFolder
{
    private readonly IOptions<TimeglassOptions> _options;
    private readonly ILogger _logger;

    public LogFolder(IOptions<TimeglassOptions> options, ILogger<LogFolder> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary><c>%LOCALAPPDATA%\Timeglass\logs</c>.</summary>
    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Timeglass",
        "logs");

    /// <summary>The folder the log files go to.</summary>
    public string Path => Resolve(_options.Value);

    /// <summary><see cref="TimeglassOptions.LogDirectory"/>, or <see cref="DefaultPath"/> if it isn't set.</summary>
    public static string Resolve(TimeglassOptions options) =>
        string.IsNullOrWhiteSpace(options.LogDirectory) ? DefaultPath : options.LogDirectory;

    /// <summary>Opens the folder in Explorer, creating it if needed. False if that failed.</summary>
    public bool TryOpen()
    {
        var path = Path;
        try
        {
            Directory.CreateDirectory(path);
            using var explorer = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or UnauthorizedAccessException)
        {
            LogOpenFailed(_logger, ex, path);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not open the log folder {Path}")]
    private static partial void LogOpenFailed(ILogger logger, Exception exception, string path);
}
