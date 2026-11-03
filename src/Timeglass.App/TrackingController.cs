using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Timeglass.Core.Tracking;

namespace Timeglass.App;

/// <summary>
/// Runs the <see cref="TrackingService"/> for the lifetime of the host, and pauses and resumes it
/// from the tray menu.
/// </summary>
/// <remarks>
/// Pausing ends the current run: the open interval is closed and flushed, and nothing is recorded
/// until tracking resumes, which starts a new run (and a new interval). Privacy controls build on
/// this in #24.
/// </remarks>
internal sealed partial class TrackingController : IHostedService, IDisposable
{
    private readonly TrackingService _service;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _stop;
    private Task? _run;

    public TrackingController(TrackingService service, ILogger<TrackingController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>Raised after tracking was paused or resumed, on the thread that did it.</summary>
    public event Action? StateChanged;

    /// <summary>True while tracking is paused (or before it started).</summary>
    public bool IsPaused => Volatile.Read(ref _run) is null;

    /// <summary>Starts tracking with the host.</summary>
    public Task StartAsync(CancellationToken cancellationToken) => ResumeAsync();

    /// <summary>Stops tracking with the host: closes the open interval and waits until it is stored.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => PauseAsync();

    /// <summary>Starts a tracking run unless one is running.</summary>
    public async Task ResumeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_run is not null)
            {
                return;
            }

            _stop = new CancellationTokenSource();
            Volatile.Write(ref _run, RunAsync(_stop.Token));
        }
        finally
        {
            _gate.Release();
        }

        LogResumed(_logger);
        StateChanged?.Invoke();
    }

    /// <summary>Ends the current run, if any, once its final changes have been stored.</summary>
    public async Task PauseAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_run is not { } run)
            {
                return;
            }

            await _stop!.CancelAsync().ConfigureAwait(false);
            await run.ConfigureAwait(false);
            _stop.Dispose();
            _stop = null;
            Volatile.Write(ref _run, null);
        }
        finally
        {
            _gate.Release();
        }

        LogPaused(_logger);
        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        _stop?.Dispose();
        _gate.Dispose();
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Off the caller's thread: the first steps of a run are synchronous.
            await Task.Run(() => _service.RunAsync(cancellationToken), CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failed run is logged; the tray stays up so the user can quit or resume.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogRunFailed(_logger, ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracking started")]
    private static partial void LogResumed(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracking paused")]
    private static partial void LogPaused(ILogger logger);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tracking stopped unexpectedly")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);
}
