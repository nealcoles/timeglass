using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Timeglass.Core.Apps;
using Timeglass.Core.Domain;
using Timeglass.Core.Ports;
using Timeglass.Core.Tracking;
using Timeglass.Testing;
using static Timeglass.Testing.TestWindows;

namespace Timeglass.Core.Tests.Tracking;

public class TrackingServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(T0);
    private readonly FakeForegroundWindowSource _foreground = new() { Current = Outlook() };
    private readonly FakeSessionEventSource _sessions = new();
    private readonly RecordingIntervalSink _sink = new();
    private readonly RecordingLogger<TrackingService> _log = new();

    private TrackingService CreateService(Func<TrackingOptions>? currentOptions = null) =>
        new(
            new TrackingEngine(new AliasAppIdentityResolver(AppAliasTable.Empty)),
            _foreground,
            new FakeIdleSource(_time),
            _sessions,
            _sink,
            _time,
            _log)
        {
            CurrentOptions = currentOptions,
        };

    /// <summary>Advances fake time one second at a time so ticks fire and get processed.</summary>
    private async Task AdvanceAsync(TimeSpan by)
    {
        for (var elapsed = TimeSpan.Zero; elapsed < by; elapsed += TimeSpan.FromSeconds(1))
        {
            _time.Advance(TimeSpan.FromSeconds(1));
            await Task.Yield();
        }

        await WaitUntilIdleAsync();
    }

    private async Task WaitUntilIdleAsync()
    {
        // Let the reader loop drain the channel.
        var before = -1;
        while (before != _sink.Changes.Count)
        {
            before = _sink.Changes.Count;
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task Tracks_switches_and_closes_everything_on_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var run = CreateService().RunAsync(cts.Token);
        await WaitUntilIdleAsync();

        await AdvanceAsync(TimeSpan.FromSeconds(30));
        _foreground.SwitchTo(Code());
        await AdvanceAsync(TimeSpan.FromSeconds(30));

        await cts.CancelAsync();
        await run;

        var intervals = _sink.Intervals;
        intervals.Select(i => i.App!.Key).ShouldBe(["olk", "code"]);
        intervals[0].Start.ShouldBe(T0);
        intervals[0].End.ShouldBe(T0.AddSeconds(30));
        intervals[1].End.ShouldBe(T0.AddSeconds(60));
        intervals.ShouldAllBe(i => !i.IsOpen);
    }

    [Fact]
    public async Task Checkpoints_reach_the_sink_while_running()
    {
        using var cts = new CancellationTokenSource();
        var run = CreateService().RunAsync(cts.Token);
        await WaitUntilIdleAsync();

        await AdvanceAsync(TimeSpan.FromSeconds(16));

        var open = _sink.Intervals.ShouldHaveSingleItem();
        open.IsOpen.ShouldBeTrue();
        open.End.ShouldBe(T0.AddSeconds(15));

        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Changed_options_apply_on_the_next_tick_without_a_restart()
    {
        // Stands in for the host's live settings: another thread swaps the options.
        var options = TrackingOptions.Default;
        using var cts = new CancellationTokenSource();
        var run = CreateService(() => Volatile.Read(ref options)).RunAsync(cts.Token);
        await WaitUntilIdleAsync();
        await AdvanceAsync(TimeSpan.FromSeconds(2));

        Volatile.Write(ref options, TrackingOptions.Default with { CheckpointInterval = TimeSpan.FromSeconds(5) });
        await AdvanceAsync(TimeSpan.FromSeconds(4));

        // With the default 15 s there would be no checkpoint yet.
        _sink.Intervals.ShouldHaveSingleItem().End.ShouldBe(T0.AddSeconds(5));

        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Unsubscribes_from_the_foreground_source_when_stopped()
    {
        using var cts = new CancellationTokenSource();
        var run = CreateService().RunAsync(cts.Token);
        await WaitUntilIdleAsync();
        _foreground.HasSubscribers.ShouldBeTrue();
        _sessions.HasSubscribers.ShouldBeTrue();

        await cts.CancelAsync();
        await run;

        _foreground.HasSubscribers.ShouldBeFalse();
        _sessions.HasSubscribers.ShouldBeFalse();
    }

    [Fact]
    public async Task Lock_and_unlock_reach_the_engine_with_the_window_read_on_unlock()
    {
        using var cts = new CancellationTokenSource();
        var run = CreateService().RunAsync(cts.Token);
        await WaitUntilIdleAsync();

        await AdvanceAsync(TimeSpan.FromSeconds(10));
        _sessions.Raise(SessionEvent.Locked);
        await AdvanceAsync(TimeSpan.FromSeconds(20));
        _foreground.Current = Code(); // changed behind the lock screen, no event
        _sessions.Raise(SessionEvent.Unlocked);
        await AdvanceAsync(TimeSpan.FromSeconds(10));

        await cts.CancelAsync();
        await run;

        _sink.Intervals.Select(i => (i.Kind, i.App?.Key, i.Start, i.End)).ShouldBe(
        [
            (IntervalKind.Active, "olk", T0, T0.AddSeconds(10)),
            (IntervalKind.Away, null, T0.AddSeconds(10), T0.AddSeconds(30)),
            (IntervalKind.Active, "code", T0.AddSeconds(30), T0.AddSeconds(40)),
        ]);
    }

    [Fact]
    public async Task Shutdown_handler_returns_only_after_everything_is_stored()
    {
        using var cts = new CancellationTokenSource();
        var run = CreateService().RunAsync(cts.Token);
        await WaitUntilIdleAsync();
        await AdvanceAsync(TimeSpan.FromSeconds(10));

        // Raised synchronously, as SystemEvents does; the OS may kill the process right after.
        await Task.Run(() => _sessions.Raise(SessionEvent.ShuttingDown), TestContext.Current.CancellationToken);

        var interval = _sink.Intervals.ShouldHaveSingleItem();
        interval.IsOpen.ShouldBeFalse();
        interval.End.ShouldBe(T0.AddSeconds(10));
        _sink.FlushCount.ShouldBe(1);

        await cts.CancelAsync();
        await run;
        _sink.FlushCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_failing_sink_does_not_stop_tracking()
    {
        using var cts = new CancellationTokenSource();
        var run = CreateService().RunAsync(cts.Token);
        await WaitUntilIdleAsync();

        _sink.FailWith = new IOException("disk full");
        _foreground.SwitchTo(Code());
        await AdvanceAsync(TimeSpan.FromSeconds(5));
        _sink.FailWith = null;

        await AdvanceAsync(TimeSpan.FromSeconds(20));
        await cts.CancelAsync();
        await run;

        run.IsCompletedSuccessfully.ShouldBeTrue();
        _sink.Intervals.ShouldContain(i => i.App!.Key == "code" && !i.IsOpen);
    }

    [Fact]
    public async Task Interval_changes_are_logged_at_debug_without_window_titles()
    {
        using var cts = new CancellationTokenSource();
        var run = CreateService().RunAsync(cts.Token);
        await WaitUntilIdleAsync();

        await AdvanceAsync(TimeSpan.FromSeconds(16));
        _foreground.SwitchTo(Code());
        await AdvanceAsync(TimeSpan.FromSeconds(5));

        await cts.CancelAsync();
        await run;

        var entries = _log.Entries;
        entries.ShouldContain(e => e.Level == LogLevel.Debug && e.Message.Contains("Opened", StringComparison.Ordinal) && e.Message.Contains("olk", StringComparison.Ordinal));
        entries.ShouldContain(e => e.Level == LogLevel.Debug && e.Message.Contains("Closed", StringComparison.Ordinal) && e.Message.Contains("olk", StringComparison.Ordinal));
        entries.ShouldContain(e => e.Level == LogLevel.Debug && e.Message.Contains("Opened", StringComparison.Ordinal) && e.Message.Contains("code", StringComparison.Ordinal));
        entries.ShouldContain(e => e.Level == LogLevel.Trace && e.Message.Contains("checkpoint", StringComparison.Ordinal));

        // Titles are private: not in the log at any level.
        string[] titles = [Outlook().Title!, Code().Title!];
        entries.ShouldAllBe(e => !titles.Any(t => e.Message.Contains(t, StringComparison.Ordinal)));
    }
    [Fact]
    public async Task Deleting_into_the_open_interval_closes_it_first_and_tracking_carries_on()
    {
        using var cts = new CancellationTokenSource();
        var service = CreateService();
        var run = service.RunAsync(cts.Token);
        await WaitUntilIdleAsync();
        await AdvanceAsync(TimeSpan.FromSeconds(30));
        _foreground.SwitchTo(Code());
        await AdvanceAsync(TimeSpan.FromSeconds(30));

        // The last 40 seconds, and a bit of the future, which can't be deleted.
        await service.DeleteRangeAsync(T0.AddSeconds(20), T0.AddSeconds(90), TestContext.Current.CancellationToken);
        await AdvanceAsync(TimeSpan.FromSeconds(10));
        await cts.CancelAsync();
        await run;

        _sink.DeletedRanges.ShouldBe([(T0.AddSeconds(20), T0.AddSeconds(60))]);
        _sink.Intervals.Select(i => (i.App?.Key, i.Start, i.End)).ShouldBe(
        [
            ("olk", T0, T0.AddSeconds(20)),
            ("code", T0.AddSeconds(60), T0.AddSeconds(70)),
        ]);
    }

    [Fact]
    public async Task Deleting_before_the_open_interval_leaves_it_running()
    {
        using var cts = new CancellationTokenSource();
        var service = CreateService();
        var run = service.RunAsync(cts.Token);
        await WaitUntilIdleAsync();
        await AdvanceAsync(TimeSpan.FromSeconds(30));
        _foreground.SwitchTo(Code());
        await AdvanceAsync(TimeSpan.FromSeconds(30));
        var open = _sink.Intervals[^1];

        await service.DeleteRangeAsync(T0, T0.AddSeconds(10), TestContext.Current.CancellationToken);

        _sink.Intervals[^1].ShouldBe(open);
        _sink.Intervals.Count.ShouldBe(2);
        await cts.CancelAsync();
        await run;
    }

    [Fact]
    public async Task Deleting_while_not_tracking_goes_straight_to_the_sink()
    {
        var service = CreateService();

        await service.DeleteRangeAsync(T0.AddHours(-2), T0.AddHours(1), TestContext.Current.CancellationToken);

        _sink.DeletedRanges.ShouldBe([(T0.AddHours(-2), T0)]);
    }

    [Fact]
    public async Task A_failed_delete_reaches_the_caller_and_tracking_carries_on()
    {
        var sink = new FailingDeleteSink(_sink);
        var service = new TrackingService(
            new TrackingEngine(new AliasAppIdentityResolver(AppAliasTable.Empty)),
            _foreground, new FakeIdleSource(_time), _sessions, sink, _time);
        using var cts = new CancellationTokenSource();
        var run = service.RunAsync(cts.Token);
        await WaitUntilIdleAsync();
        await AdvanceAsync(TimeSpan.FromSeconds(30));

        await Should.ThrowAsync<IOException>(() =>
            service.DeleteRangeAsync(T0, T0.AddSeconds(30), TestContext.Current.CancellationToken));
        await AdvanceAsync(TimeSpan.FromSeconds(16));

        _sink.Intervals[^1].IsOpen.ShouldBeTrue();
        await cts.CancelAsync();
        await run;
        run.IsCompletedSuccessfully.ShouldBeTrue();
    }

    private sealed class FailingDeleteSink(IIntervalSink inner) : IIntervalSink
    {
        public ValueTask ApplyAsync(IReadOnlyList<IntervalChange> changes, CancellationToken cancellationToken) =>
            inner.ApplyAsync(changes, cancellationToken);

        public ValueTask FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

        public ValueTask DeleteRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
            throw new IOException("disk full");
    }
}
