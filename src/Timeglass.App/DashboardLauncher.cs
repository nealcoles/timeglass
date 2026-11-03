using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Timeglass.App;

/// <summary>Opens the dashboard in the default browser.</summary>
internal sealed partial class DashboardLauncher
{
    private readonly IOptions<TimeglassOptions> _options;
    private readonly ILogger _logger;

    public DashboardLauncher(IOptions<TimeglassOptions> options, ILogger<DashboardLauncher> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>The dashboard address, or <see langword="null"/> while there is none (see #27, #30).</summary>
    public Uri? Url =>
        _options.Value.DashboardUrl is { IsAbsoluteUri: true } url && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
            ? url
            : null;

    /// <summary>Opens the dashboard. False if there is no dashboard or the browser didn't start.</summary>
    public bool TryOpen()
    {
        if (Url is not { } url)
        {
            return false;
        }

        try
        {
            using var browser = Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true });
            return true;
        }
        catch (Win32Exception ex)
        {
            LogOpenFailed(_logger, ex, url);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not open the dashboard at {Url}")]
    private static partial void LogOpenFailed(ILogger logger, Exception exception, Uri url);
}
