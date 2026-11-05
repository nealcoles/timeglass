using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Timeglass.Core.Tracking;

namespace Timeglass.App;

/// <summary>
/// Runs the <see cref="TrackingService"/> for the lifetime of the host, and pauses and resumes it
/// from the tray menu.
/// </summary>
/// <remarks>
/// <para>
/// Pausing ends the current run: the open interval is closed and flushed, and nothing is recorded
/// until tracking resumes, which starts a new run (and a new interval). Paused time is a gap in
/// the data, not an interval of its own.
/// </para>
/// <para>
/// A pause can have an end (<see cref="PausedUntil"/>), after which tracking resumes by itself.
/// The end is checked against the wall clock at least every <see cref="ResumeCheckInterval"/>,
/// so a pause still ends on time after the PC slept through it. A pause does not survive a
/// restart of the app.
/// </para>
/// </remarks>
internal sealed partial class TrackingController : IHostedService, IDisposable
{
    /// <summary>How often a pause with an end checks whether it is over.</summary>
    public static readonly TimeSpan ResumeCheckInterval = TimeSpan.FromSeconds(30);

    private readonly TrackingService _service;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private CancellationTokenSource? _stop;
    private Task? _run;
    private PauseEnd? _pausedUntil;
    private ITimer? _resumeTimer;

    public TrackingController(TrackingService service, TimeProvider time, ILogger<TrackingController> logger)
    {
        _service = service;
        _time = time;
        _logger = logger;
    }

    /// <summary>Raised after tracking was paused or resumed, or the pause's end changed, on the thread that did it.</summary>
    public event Action? StateChanged;

    /// <summary>True while tracking is paused (or before it started).</summary>
    public bool IsPaused => Volatile.Read(ref _run) is null;

    /// <summary>When the current pause ends by itself; <see langword="null"/> if it doesn't, or if not paused.</summary>
    public DateTimeOffset? PausedUntil => Volatile.Read(ref _pausedUntil)?.At;

    /// <summary>Starts tracking with the host.</summary>
    public Task StartAsync(CancellationToken cancellationToken) => ResumeAsync();

    /// <summary>Stops tracking with the host: closes the open interval and waits until it is stored.</summary>
    public Task StopAsync(CancellationToken cancellationToken) => PauseAsync();

    /// <summary>Starts a tracking run unless one is running, and cancels any pause end.</summary>
    public Task ResumeAsync() => ResumeAsync(onlyIfPausedUntil: null);

    /// <summary>Pauses for <paramref name="length"/>; see <see cref="PauseAsync(DateTimeOffset?)"/>.</summary>
    public Task PauseAsync(PauseLength length) =>
        PauseAsync(PauseSchedule.EndOf(length, _time.GetUtcNow(), _time.LocalTimeZone));

    /// <summary>
    /// Ends the current run, if any, once its final changes have been stored. Tracking resumes by
    /// itself at <paramref name="until"/>, or stays paused until <see cref="ResumeAsync()"/> when
    /// it is <see langword="null"/>. Pausing while paused only changes when the pause ends.
    /// </summary>
    public async Task PauseAsync(DateTimeOffset? until = null)
    {
        bool stopped;
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            stopped = await StopRunAsync().ConfigureAwait(false);
            if (!stopped && PausedUntil == until)
            {
                return;
            }

            SetPauseEnd(until);
        }
        finally
        {
            _gate.Release();
        }

        if (stopped)
        {
            LogPaused(_logger, until);
        }

        StateChanged?.Invoke();
    }

    public void Dispose()
    {
        _resumeTimer?.Dispose();
        _stop?.Dispose();
        _gate.Dispose();
    }

    /// <param name="onlyIfPausedUntil">When set, resumes only if the pause still ends then (a timer firing late).</param>
    private async Task ResumeAsync(PauseEnd? onlyIfPausedUntil)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (onlyIfPausedUntil is not null && !ReferenceEquals(onlyIfPausedUntil, _pausedUntil))
            {
                return;
            }

            SetPauseEnd(null);
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

    /// <summary>Ends the run, if any. Call holding the gate.</summary>
    private async Task<bool> StopRunAsync()
    {
        if (_run is not { } run)
        {
            return false;
        }

        await _stop!.CancelAsync().ConfigureAwait(false);
        await run.ConfigureAwait(false);
        _stop.Dispose();
        _stop = null;
        Volatile.Write(ref _run, null);
        return true;
    }

    /// <summary>Sets (or clears) the pause end and its timer. Call holding the gate.</summary>
    private void SetPauseEnd(DateTimeOffset? until)
    {
        _resumeTimer?.Dispose();
        _resumeTimer = null;

        var end = until is { } at ? new PauseEnd(at) : null;
        Volatile.Write(ref _pausedUntil, end);
        if (end is not null)
        {
            _resumeTimer = _time.CreateTimer(_ => CheckPauseEnd(end), null, NextCheck(end), Timeout.InfiniteTimeSpan);
        }
    }

    private TimeSpan NextCheck(PauseEnd end)
    {
        var left = end.At - _time.GetUtcNow();
        return left <= TimeSpan.Zero ? TimeSpan.Zero : left < ResumeCheckInterval ? left : ResumeCheckInterval;
    }

    /// <summary>Timer callback: resumes once the end has passed on the wall clock, else checks again.</summary>
    private void CheckPauseEnd(PauseEnd end)
    {
        if (_time.GetUtcNow() < end.At)
        {
            try
            {
                _resumeTimer?.Change(NextCheck(end), Timeout.InfiniteTimeSpan);
            }
            catch (ObjectDisposedException)
            {
                // Resumed or re-paused meanwhile.
            }

            return;
        }

        _ = ResumeWhenPauseEndsAsync(end);
    }

    private async Task ResumeWhenPauseEndsAsync(PauseEnd end)
    {
        try
        {
            await ResumeAsync(onlyIfPausedUntil: end).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A timer callback has no caller to throw to.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogAutoResumeFailed(_logger, ex);
        }
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Tracking paused until {Until}")]
    private static partial void LogPaused(ILogger logger, DateTimeOffset? until);

    [LoggerMessage(Level = LogLevel.Error, Message = "Could not resume tracking at the end of a pause")]
    private static partial void LogAutoResumeFailed(ILogger logger, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tracking stopped unexpectedly")]
    private static partial void LogRunFailed(ILogger logger, Exception exception);

    /// <summary>A pause end; compared by reference, so a late timer can tell its pause was replaced.</summary>
    private sealed class PauseEnd(DateTimeOffset at)
    {
        public DateTimeOffset At { get; } = at;
    }
}
