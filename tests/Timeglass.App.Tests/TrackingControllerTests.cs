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
        _controller = new TrackingController(service, NullLogger<TrackingController>.Instance);
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
}
