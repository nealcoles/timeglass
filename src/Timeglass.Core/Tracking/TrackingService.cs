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
    private readonly IIntervalSink _sink;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    /// <summary>Creates the service.</summary>
    public TrackingService(
        TrackingEngine engine,
        IForegroundWindowSource foreground,
        IIdleSource idle,
        IIntervalSink sink,
        TimeProvider time,
        ILogger<TrackingService>? logger = null)
    {
        _engine = engine;
        _foreground = foreground;
        _idle = idle;
        _sink = sink;
        _time = time;
        _logger = logger ?? NullLogger<TrackingService>.Instance;
    }

    /// <summary>
    /// Tracks until <paramref name="cancellationToken"/> is cancelled, then closes the open
    /// interval and returns once the final changes have reached the sink.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<TrackingEvent>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
        });

        void OnForeground(WindowSnapshot? window) =>
            channel.Writer.TryWrite(new ForegroundChanged(_time.GetUtcNow(), window));

        _foreground.ForegroundChanged += OnForeground;
        using var timer = _time.CreateTimer(
            _ => channel.Writer.TryWrite(new Tick(_time.GetUtcNow(), _idle.GetLastInputTime())),
            state: null,
            dueTime: _engine.Options.TickInterval,
            period: _engine.Options.TickInterval);

        try
        {
            await ApplyAsync(new TrackingStarted(_time.GetUtcNow(), _foreground.GetCurrent()), CancellationToken.None)
                .ConfigureAwait(false);

            try
            {
                await foreach (var trackingEvent in channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
                {
                    await ApplyAsync(trackingEvent, CancellationToken.None).ConfigureAwait(false);
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
            channel.Writer.TryComplete();
        }

        // Drain anything that arrived before shutdown, then close everything.
        while (channel.Reader.TryRead(out var remaining))
        {
            await ApplyAsync(remaining, CancellationToken.None).ConfigureAwait(false);
        }

        await ApplyAsync(new TrackingStopped(_time.GetUtcNow()), CancellationToken.None).ConfigureAwait(false);
    }

    private async ValueTask ApplyAsync(TrackingEvent trackingEvent, CancellationToken cancellationToken)
    {
        var changes = _engine.Handle(trackingEvent);
        if (changes.Count == 0)
        {
            return;
        }

        try
        {
            await _sink.ApplyAsync(changes, cancellationToken).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // A failing sink must not stop tracking; the next checkpoint retries the open interval.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _logger.LogError(ex, "Failed to store {Count} interval change(s)", changes.Count);
        }
    }
}
