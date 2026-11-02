using Timeglass.Core.Apps;
using Timeglass.Core.Domain;
using Timeglass.Core.Ports;
using Timeglass.Core.Tracking;
using Timeglass.Testing;

namespace Timeglass.Core.Tests.Tracking;

/// <summary>
/// Feeds the engine random event sequences and checks invariants that must hold for any input.
/// Seeds are fixed, so a failure always reproduces with the seed in the test name.
/// </summary>
public class TrackingEnginePropertyTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private static readonly WindowSnapshot?[] Pool =
    [
        TestWindows.Outlook("Inbox"),
        TestWindows.Outlook("RE: Budget"),
        TestWindows.Code(),
        TestWindows.Teams(),
        TestWindows.Browser(),
        null,
    ];

    private static readonly SessionEvent[] LockOrSuspend = [SessionEvent.Locked, SessionEvent.Suspending];

    private static readonly SessionEvent[] UnlockOrResume = [SessionEvent.Unlocked, SessionEvent.Resumed];

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 1; seed <= 200; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Intervals_are_contiguous_non_overlapping_and_cover_the_tracked_time(int seed)
    {
        var random = new Random(seed);
        var engine = new TrackingEngine(new AliasAppIdentityResolver(AppAliasTable.Empty), new TrackingOptions
        {
            MinimumInterval = TimeSpan.FromMilliseconds(random.Next(0, 3000)),
            CheckpointInterval = TimeSpan.FromSeconds(random.Next(1, 30)),
            IdleThreshold = random.Next(4) == 0 ? null : TimeSpan.FromSeconds(random.Next(5, 120)),
            ClockJumpThreshold = random.Next(4) == 0 ? null : TimeSpan.FromSeconds(random.Next(30, 300)),
        });
        var timeline = new IntervalTimeline();

        var now = T0;
        var maxSeen = T0;
        var lastInput = T0;
        timeline.Apply(engine.Handle(new TrackingStarted(now, Pick(random))));

        var steps = random.Next(1, 400);
        for (var i = 0; i < steps; i++)
        {
            // Mostly forward in time, sometimes the same instant, occasionally backwards or a jump (sleep).
            var deltaMs = random.Next(40) switch
            {
                < 4 => -random.Next(0, 5000),
                < 8 => 0,
                39 => random.Next(60_000, 3_600_000),
                _ => random.Next(1, 20_000),
            };
            now = now.AddMilliseconds(deltaMs);
            if (now > maxSeen)
            {
                maxSeen = now;
            }

            // The user is either active (input now) or has walked away (input stays put).
            if (random.Next(4) != 0)
            {
                lastInput = now;
            }

            TrackingEvent e = random.Next(12) switch
            {
                < 4 => new Tick(now, lastInput),
                4 => new SessionChanged(now, LockOrSuspend[random.Next(2)]),
                5 => new SessionChanged(now, UnlockOrResume[random.Next(2)], Pick(random)),
                _ => new ForegroundChanged(now, Pick(random)),
            };
            timeline.Apply(engine.Handle(e));
        }

        var stopAt = maxSeen.AddSeconds(random.Next(0, 60));
        timeline.Apply(engine.Handle(random.Next(2) == 0
            ? new TrackingStopped(stopAt)
            : new SessionChanged(stopAt, SessionEvent.ShuttingDown)));

        var intervals = timeline.Intervals;

        intervals.ShouldNotBeEmpty();
        intervals.ShouldAllBe(iv => !iv.IsOpen, "everything is closed after stop");
        intervals.ShouldAllBe(iv => iv.End >= iv.Start, "no negative intervals");
        intervals[0].Start.ShouldBe(T0);
        intervals[^1].End.ShouldBe(stopAt);

        for (var i = 1; i < intervals.Count; i++)
        {
            intervals[i].Start.ShouldBe(intervals[i - 1].End, $"interval {i} must start where {i - 1} ended");
        }

        var total = intervals.Aggregate(TimeSpan.Zero, (sum, iv) => sum + iv.Duration);
        total.ShouldBe(stopAt - T0);
    }

    private static WindowSnapshot? Pick(Random random) => Pool[random.Next(Pool.Length)];
}
