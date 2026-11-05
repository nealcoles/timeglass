using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Timeglass.Core.Domain;
using Timeglass.Core.Ports;

namespace Timeglass.Core.Tracking;

/// <summary>
/// Runs a <see cref="TrackingEngine"/>: collects events from the OS ports and a timer into one
/// channel, feeds them to the engine on a single reader, and forwards the changes to the sink.
/// </summary>
/// <remarks>
/// Ports may raise events on any thread. Everything after the channel runs sequentially,
/// so the engine needs no locking. Interval changes are logged at Debug (checkpoints at Trace)
/// with the app and kind only: window titles are private and never logged.
/// </remarks>
public sealed partial class TrackingService
{
    private readonly TrackingEngine _engine;
    private readonly IForegroundWindowSource _foreground;
    private readonly IIdleSource _idle;
    private readonly ISessionEventSource _sessions;
    private readonly IIntervalSink _sink;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    // The running run's queue, or null between runs.
    private Channel<Queued>? _queue;

    /// <summary>Creates the service.</summary>
    public TrackingService(
        TrackingEngine engine,
        IForegroundWindowSource foreground,
        IIdleSource idle,
        ISessionEventSource sessions,
        IIntervalSink sink,
        TimeProvider time,
        ILogger<TrackingService>? logger = null)
    {
        _engine = engine;
        _foreground = foreground;
        _idle = idle;
        _sessions = sessions;
        _sink = sink;
        _time = time;
        _logger = logger ?? NullLogger<TrackingService>.Instance;
    }

    /// <summary>
    /// How long a <see cref="SessionEvent.ShuttingDown"/> handler blocks while the final changes
    /// are written. Windows may end the process as soon as the handler returns.
    /// </summary>
    public TimeSpan ShutdownFlushTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Returns the options to use, read before every event so changed settings apply on the next
    /// tick without a restart. Called on the reader, so it must be cheap and thread-safe. When
    /// <see langword="null"/>, the engine keeps the options it was created with.
    /// </summary>
    /// <remarks><see cref="TrackingOptions.TickInterval"/> is read once, when a run starts.</remarks>
    public Func<TrackingOptions>? CurrentOptions { get; init; }

    /// <summary>
    /// Tracks until <paramref name="cancellationToken"/> is cancelled, then closes the open
    /// interval and returns once the final changes have reached the sink.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (CurrentOptions is { } current)
        {
            _engine.Options = current();
        }

        var channel = Channel.CreateUnbounded<Queued>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });
        Volatile.Write(ref _queue, channel);

        void OnForeground(WindowSnapshot? window) =>
            channel.Writer.TryWrite(new Queued(new ForegroundChanged(_time.GetUtcNow(), window)));

        void OnSession(SessionEvent sessionEvent)
        {
            var at = _time.GetUtcNow();
            var window = sessionEvent is SessionEvent.Unlocked or SessionEvent.Resumed ? _foreground.GetCurrent() : null;
            var queued = new Queued(
                new SessionChanged(at, sessionEvent, window),
                sessionEvent == SessionEvent.ShuttingDown ? new TaskCompletionSource() : null);

            if (channel.Writer.TryWrite(queued) && queued.Done is not null
                && !queued.Done.Task.Wait(ShutdownFlushTimeout))
            {
                _logger.LogWarning("Interval changes were not stored within {Timeout} on shutdown", ShutdownFlushTimeout);
            }
        }

        _foreground.ForegroundChanged += OnForeground;
        _sessions.SessionChanged += OnSession;
        using var timer = _time.CreateTimer(
            _ => channel.Writer.TryWrite(new Queued(new Tick(_time.GetUtcNow(), _idle.GetLastInputTime()))),
            state: null,
            dueTime: _engine.Options.TickInterval,
            period: _engine.Options.TickInterval);

        try
        {
            await ApplyAsync(new Queued(new TrackingStarted(_time.GetUtcNow(), _foreground.GetCurrent())))
                .ConfigureAwait(false);

            try
            {
                await foreach (var queued in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    await ApplyAsync(queued).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal shutdown.
            }
        }
        finally
        {
            _foreground.ForegroundChanged -= OnForeground;
            _sessions.SessionChanged -= OnSession;
            channel.Writer.TryComplete();
            Interlocked.CompareExchange(ref _queue, null, channel);
        }

        // Drain anything that arrived before shutdown, then close everything.
        while (channel.Reader.TryRead(out var remaining))
        {
            await ApplyAsync(remaining).ConfigureAwait(false);
        }

        await ApplyAsync(new Queued(new TrackingStopped(_time.GetUtcNow()))).ConfigureAwait(false);
        await FlushAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes everything recorded in [<paramref name="from"/>, <paramref name="to"/>), cutting
    /// intervals that overlap its edges. The future can't be deleted, so the range ends now at
    /// the latest.
    /// </summary>
    /// <remarks>
    /// While tracking, the deletion runs in order with the tracking events. If the range reaches
    /// into the open interval, that interval is closed first and a new one is started for the
    /// window in front, so the tracker never writes deleted time back. An idle or locked user is
    /// treated as present again from that moment, until the next tick says otherwise.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="to"/> is before <paramref name="from"/>.</exception>
    public async Task DeleteRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken = default)
    {
        if (to < from)
        {
            throw new ArgumentException("The range must not end before it starts.", nameof(to));
        }

        var queued = new Queued(null, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously), new DeleteRequest(from, to));
        if (Volatile.Read(ref _queue) is { } queue && queue.Writer.TryWrite(queued))
        {
            // The reader (or the drain after cancellation) runs it.
            await queued.Done!.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        // Not tracking: nothing is open.
        var now = _time.GetUtcNow();
        await _sink.DeleteRangeAsync(from, to < now ? to : now, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask ApplyAsync(Queued queued)
    {
        if (queued.Delete is { } delete)
        {
            await DeleteWhileTrackingAsync(delete, queued.Done!).ConfigureAwait(false);
            return;
        }

        try
        {
            if (CurrentOptions is { } current)
            {
                _engine.Options = current();
            }

            var changes = _engine.Handle(queued.Event!);
            if (changes.Count > 0)
            {
                LogChanges(changes);
                await StoreAsync(changes).ConfigureAwait(false);
            }

            if (queued.Done is not null)
            {
                await FlushAsync().ConfigureAwait(false);
            }
        }
        finally
        {
            queued.Done?.TrySetResult();
        }
    }

    private async ValueTask DeleteWhileTrackingAsync(DeleteRequest delete, TaskCompletionSource done)
    {
        var now = _time.GetUtcNow();
        var to = delete.To < now ? delete.To : now;
        var restart = _engine.IsTracking && _engine.Current is { } open && open.Start < to;

        try
        {
            if (restart)
            {
                await ApplyAsync(new Queued(new TrackingStopped(now))).ConfigureAwait(false);
            }

            await _sink.DeleteRangeAsync(delete.From, to, CancellationToken.None).ConfigureAwait(false);
            done.TrySetResult();
        }
#pragma warning disable CA1031 // The caller gets the error; tracking carries on.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            done.TrySetException(ex);
        }
        finally
        {
            if (restart)
            {
                await ApplyAsync(new Queued(new TrackingStarted(now, _foreground.GetCurrent()))).ConfigureAwait(false);
            }
        }
    }

    private async ValueTask StoreAsync(IReadOnlyList<IntervalChange> changes)
    {
        try
        {
            await _sink.ApplyAsync(changes, CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failing sink must not stop tracking; the next checkpoint retries the open interval.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Failed to store {Count} interval change(s)", changes.Count);
        }
    }

    private async ValueTask FlushAsync()
    {
        try
        {
            await _sink.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Shutting down either way; log and carry on.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Failed to flush interval changes");
        }
    }

    private void LogChanges(IReadOnlyList<IntervalChange> changes)
    {
        foreach (var (kind, interval) in changes)
        {
            var app = interval.App?.Key ?? "-";
            if (kind == IntervalChangeKind.Updated)
            {
                LogCheckpoint(_logger, interval.Kind, app, interval.End);
            }
            else
            {
                LogIntervalChanged(_logger, kind, interval.Kind, app, kind == IntervalChangeKind.Opened ? interval.Start : interval.End);
            }
        }
    }

    // Never add the window title to these: logs get attached to bug reports.
    [LoggerMessage(Level = LogLevel.Debug, Message = "Interval {Change}: {Kind} {App} at {At:O}")]
    private static partial void LogIntervalChanged(ILogger logger, IntervalChangeKind change, IntervalKind kind, string app, DateTimeOffset at);

    [LoggerMessage(Level = LogLevel.Trace, Message = "Interval checkpoint: {Kind} {App} until {End:O}")]
    private static partial void LogCheckpoint(ILogger logger, IntervalKind kind, string app, DateTimeOffset end);

    /// <summary>
    /// An event (or a deletion) waiting for the reader, with an optional signal for when it has
    /// been stored.
    /// </summary>
    private sealed record Queued(TrackingEvent? Event, TaskCompletionSource? Done = null, DeleteRequest? Delete = null);

    private sealed record DeleteRequest(DateTimeOffset From, DateTimeOffset To);
}
