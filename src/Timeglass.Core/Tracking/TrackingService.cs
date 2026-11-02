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
/// so the engine needs no locking.
/// </remarks>
public sealed class TrackingService
{
    private readonly TrackingEngine _engine;
    private readonly IForegroundWindowSource _foreground;
    private readonly IIdleSource _idle;
    private readonly ISessionEventSource _sessions;
    private readonly IIntervalSink _sink;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

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
    /// Tracks until <paramref name="cancellationToken"/> is cancelled, then closes the open
    /// interval and returns once the final changes have reached the sink.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<Queued>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

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
        }

        // Drain anything that arrived before shutdown, then close everything.
        while (channel.Reader.TryRead(out var remaining))
        {
            await ApplyAsync(remaining).ConfigureAwait(false);
        }

        await ApplyAsync(new Queued(new TrackingStopped(_time.GetUtcNow()))).ConfigureAwait(false);
    }

    private async ValueTask ApplyAsync(Queued queued)
    {
        try
        {
            var changes = _engine.Handle(queued.Event);
            if (changes.Count > 0)
            {
                await StoreAsync(changes).ConfigureAwait(false);
            }
        }
        finally
        {
            queued.Done?.TrySetResult();
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

    /// <summary>An event waiting for the reader, with an optional signal for when it has been stored.</summary>
    private sealed record Queued(TrackingEvent Event, TaskCompletionSource? Done = null);
}
