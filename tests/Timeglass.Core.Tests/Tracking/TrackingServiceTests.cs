using Microsoft.Extensions.Time.Testing;
using Timeglass.Core.Tracking;
using Timeglass.Testing;
using static Timeglass.Testing.TestWindows;

namespace Timeglass.Core.Tests.Tracking;

public class TrackingServiceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(T0);
    private readonly FakeForegroundWindowSource _foreground = new() { Current = Outlook() };
    private readonly RecordingIntervalSink _sink = new();

    private TrackingService CreateService() =>
        new(
            new TrackingEngine(new ExeNameAppIdentityResolver()),
            _foreground,
            new FakeIdleSource(_time),
            _sink,
            _time);

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
    public async Task Unsubscribes_from_the_foreground_source_when_stopped()
    {
        using var cts = new CancellationTokenSource();
        var run = CreateService().RunAsync(cts.Token);
        await WaitUntilIdleAsync();
        _foreground.HasSubscribers.ShouldBeTrue();

        await cts.CancelAsync();
        await run;

        _foreground.HasSubscribers.ShouldBeFalse();
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
}
