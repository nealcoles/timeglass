using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Timeglass.App;

/// <summary>Opens the dashboard in the default browser.</summary>
internal sealed partial class DashboardLauncher
{
    private readonly IOptions<TimeglassOptions> _options;
    private readonly ApiServer _api;
    private readonly ILogger _logger;

    public DashboardLauncher(IOptions<TimeglassOptions> options, ApiServer api, ILogger<DashboardLauncher> logger)
    {
        _options = options;
        _api = api;
        _logger = logger;
    }

    /// <summary>
    /// The dashboard address: <see cref="TimeglassOptions.DashboardUrl"/> if set, else the API's
    /// sign-in link, which carries this run's token. <see langword="null"/> while there is none.
    /// </summary>
    public Uri? Url =>
        _options.Value.DashboardUrl is { IsAbsoluteUri: true } url && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps)
            ? url
            : _api.SignInUrl;

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
            LogOpenFailed(_logger, ex, url.GetLeftPart(UriPartial.Path));
            return false;
        }
    }

    // Without the query: the sign-in link's token must not end up in the log.
    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not open the dashboard at {Url}")]
    private static partial void LogOpenFailed(ILogger logger, Exception exception, string url);
}
