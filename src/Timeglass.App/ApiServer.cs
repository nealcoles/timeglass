using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Timeglass.Api;
using Timeglass.App.Settings;
using Timeglass.Core.Ports;
using Timeglass.Storage;

namespace Timeglass.App;

/// <summary>
/// Runs the local HTTP API (#27) on 127.0.0.1 behind its access token (#29) for the lifetime of
/// the host, and writes the runtime file other tools find it by.
/// </summary>
/// <remarks>
/// The port is the user's <c>ApiPort</c> setting, or a free one if something else has it; a
/// change to the setting applies on the next start. If the server can't start at all, the app
/// still tracks; only the dashboard is unavailable.
/// </remarks>
internal sealed partial class ApiServer : IHostedService, IAsyncDisposable
{
    private readonly IServiceProvider _services;
    private readonly LiveSettings _settings;
    private readonly IOptions<TimeglassOptions> _options;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;
    private WebApplication? _app;
    private ApiSecurityOptions? _security;

    public ApiServer(
        IServiceProvider services,
        LiveSettings settings,
        IOptions<TimeglassOptions> options,
        ILoggerFactory loggerFactory,
        ILogger<ApiServer> logger)
    {
        _services = services;
        _settings = settings;
        _options = options;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <summary>The address that signs the browser in and opens the dashboard; <see langword="null"/> while the server isn't running.</summary>
    public Uri? SignInUrl => Volatile.Read(ref _security) is { } security ? ApiSecurity.SignInUrl(security) : null;

    private string RuntimeFilePath => _options.Value.ApiRuntimeFilePath ?? ApiRuntimeFile.DefaultPath;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var preferred = _settings.Current.Settings.ApiPort;
        WebApplication? app = null;
        try
        {
            var security = new ApiSecurityOptions(LoopbackPort.Choose(preferred), ApiAccessToken.Create());
            app = TimeglassApiServer.Build(security, _loggerFactory, services =>
            {
                services.AddSingleton(_services.GetRequiredService<TimeProvider>());
                services.AddSingleton<IReportingQueries>(_services.GetRequiredService<IntervalRepository>());
                services.AddSingleton<ITrackingState>(_services.GetRequiredService<TrackingController>());
            });
            await app.StartAsync(cancellationToken).ConfigureAwait(false);
            ApiRuntimeFile.Write(RuntimeFilePath, new ApiRuntimeInfo(security.Port, security.Token.Value, Environment.ProcessId));

            _app = app;
            app = null;
            Volatile.Write(ref _security, security);
            LogStarted(_logger, security.Port, preferred, RuntimeFilePath);
        }
#pragma warning disable CA1031 // The tracker matters more than the dashboard: log and keep going.
        catch (Exception ex) when (ex is not OperationCanceledException)
#pragma warning restore CA1031
        {
            LogStartFailed(_logger, ex, preferred);
        }
        finally
        {
            if (app is not null)
            {
                await app.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref _security, null) is null)
        {
            return;
        }

        try
        {
            ApiRuntimeFile.Delete(RuntimeFilePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogDeleteFailed(_logger, ex, RuntimeFilePath);
        }

        if (_app is not null)
        {
            await _app.StopAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null)
        {
            await _app.DisposeAsync().ConfigureAwait(false);
            _app = null;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "API listening on http://127.0.0.1:{Port} (setting: {Preferred}); runtime file {Path}")]
    private static partial void LogStarted(ILogger logger, int port, int preferred, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not start the API (port setting {Preferred}); the dashboard is unavailable, tracking goes on")]
    private static partial void LogStartFailed(ILogger logger, Exception exception, int preferred);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Could not delete the API runtime file {Path}")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, string path);
}
