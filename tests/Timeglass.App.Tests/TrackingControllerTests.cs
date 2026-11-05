using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Timeglass.Core.Apps;
using Timeglass.Core.Domain;
using Timeglass.Core.Tracking;
using Timeglass.Testing;
using static Timeglass.Testing.TestWindows;

namespace Timeglass.App.Tests;

public sealed class TrackingControllerTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 3, 8, 0, 0, TimeSpan.Zero);

    private readonly FakeTimeProvider _time = new(T0);
    private readonly RecordingIntervalSink _sink = new();
    private readonly TrackingController _controller;

    public TrackingControllerTests()
    {
        var service = new TrackingService(
            new TrackingEngine(new AliasAppIdentityResolver(AppAliasTable.Empty)),
            new FakeForegroundWindowSource { Current = Code() },
            new FakeIdleSource(_time),
            new FakeSessionEventSource(),
            _sink,
            _time);
        _controller = new TrackingController(service, _time, NullLogger<TrackingController>.Instance);
    }

    public void Dispose() => _controller.Dispose();

    [Fact]
    public async Task Is_paused_until_the_host_starts_it()
    {
        _controller.IsPaused.ShouldBeTrue();

        await _controller.StartAsync(TestContext.Current.CancellationToken);

        _controller.IsPaused.ShouldBeFalse();
        await _controller.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Pausing_closes_and_flushes_the_open_interval()
    {
        await _controller.StartAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(1));

        await _controller.PauseAsync();

        _controller.IsPaused.ShouldBeTrue();
        _sink.Intervals.ShouldHaveSingleItem().IsOpen.ShouldBeFalse();
        _sink.FlushCount.ShouldBeGreaterThan(0);
    }

    [Fact]
    public async Task Resuming_starts_a_new_interval()
    {
        await _controller.StartAsync(TestContext.Current.CancellationToken);
        _time.Advance(TimeSpan.FromMinutes(1));
        await _controller.PauseAsync();
        _time.Advance(TimeSpan.FromMinutes(10));

        await _controller.ResumeAsync();
        _time.Advance(TimeSpan.FromMinutes(1));
        await _controller.StopAsync(TestContext.Current.CancellationToken);

        var intervals = _sink.Intervals;
        intervals.Count.ShouldBe(2);
        intervals.ShouldAllBe(i => i.Kind == IntervalKind.Active && !i.IsOpen);
        intervals[0].End.ShouldBe(T0.AddMinutes(1));
        // The new run starts in the background, so its first event may come a little later.
        intervals[1].Start.ShouldBeGreaterThanOrEqualTo(T0.AddMinutes(11));
    }

    [Fact]
    public async Task Raises_state_changes_only_when_the_state_changes()
    {
        var changes = 0;
        _controller.StateChanged += () => changes++;

        await _controller.ResumeAsync();
        await _controller.ResumeAsync();
        await _controller.PauseAsync();
        await _controller.PauseAsync();

        changes.ShouldBe(2);
    }
    [Fact]
    public async Task A_timed_pause_resumes_by_itself_when_it_ends()
    {
        await _controller.StartAsync(TestContext.Current.CancellationToken);
        await _controller.PauseAsync(PauseLength.ThirtyMinutes);

        _controller.IsPaused.ShouldBeTrue();
        _controller.PausedUntil.ShouldBe(_time.GetUtcNow().AddMinutes(30));

        for (var i = 0; i < 59; i++)
        {
            _time.Advance(TimeSpan.FromSeconds(30));
        }

        _controller.IsPaused.ShouldBeTrue();
        _time.Advance(TimeSpan.FromSeconds(30));
        await WaitUntilAsync(() => !_controller.IsPaused);

        _controller.PausedUntil.ShouldBeNull();
        await _controller.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_timed_pause_ends_on_time_even_if_the_clock_jumps_past_it()
    {
        // Sleep: the wall clock moves on while timers don't fire.
        await _controller.StartAsync(TestContext.Current.CancellationToken);
        await _controller.PauseAsync(_time.GetUtcNow().AddHours(1));

        _time.Advance(TimeSpan.FromHours(3));
        await WaitUntilAsync(() => !_controller.IsPaused);

        await _controller.StopAsync(TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task Resuming_early_cancels_the_pause_end()
    {
        await _controller.StartAsync(TestContext.Current.CancellationToken);
        await _controller.PauseAsync(PauseLength.OneHour);
        await _controller.ResumeAsync();
        await _controller.PauseAsync();

        _time.Advance(TimeSpan.FromHours(2));
        await Task.Delay(100, TestContext.Current.CancellationToken);

        _controller.IsPaused.ShouldBeTrue();
        _controller.PausedUntil.ShouldBeNull();
    }

    [Fact]
    public async Task Pausing_while_paused_changes_when_the_pause_ends()
    {
        var changes = 0;
        _controller.StateChanged += () => changes++;

        await _controller.PauseAsync(); // not started, no end: nothing changes
        await _controller.PauseAsync(PauseLength.OneHour);
        await _controller.PauseAsync(PauseLength.OneHour); // the same end again

        _controller.PausedUntil.ShouldBe(_time.GetUtcNow().AddHours(1));
        changes.ShouldBe(1);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }

        condition().ShouldBeTrue();
    }
}
