using Timeglass.Core.Domain;
using Timeglass.Core.Tracking;
using Timeglass.Testing;
using static Timeglass.Testing.TestWindows;

namespace Timeglass.Core.Tests.Tracking;

public class IdleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);
    private static readonly TimeSpan Threshold = TimeSpan.FromMinutes(5);

    private readonly TrackingEngine _engine = new(
        new ExeNameAppIdentityResolver(),
        TrackingOptions.Default with
        {
            IdleThreshold = TimeSpan.FromMinutes(5),
            CheckpointInterval = TimeSpan.FromDays(1), // keep checkpoints out of these tests
            ClockJumpThreshold = null,                 // events here are minutes apart
        });

    private readonly IntervalTimeline _timeline = new();

    private static DateTimeOffset At(double minutes) => T0.AddMinutes(minutes);

    private IReadOnlyList<IntervalChange> Send(TrackingEvent e)
    {
        var changes = _engine.Handle(e);
        _timeline.Apply(changes);
        return changes;
    }

    /// <summary>A tick at <paramref name="at"/> where the last input was at <paramref name="lastInput"/>.</summary>
    private IReadOnlyList<IntervalChange> Tick(double at, double lastInput) =>
        Send(new Tick(At(at), At(lastInput)));

    private IReadOnlyList<(IntervalKind Kind, string? App, DateTimeOffset Start, DateTimeOffset End)> Summary() =>
        [.. _timeline.Intervals.Select(i => (i.Kind, i.App?.Key, i.Start, i.End))];

    [Fact]
    public void Idle_over_the_threshold_trims_the_active_interval_back_to_the_last_input()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        Tick(at: 10, lastInput: 10);
        Tick(at: 17, lastInput: 12); // 5 min without input -> away since 12

        Send(new TrackingStopped(At(20)));

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "olk", At(0), At(12)),
            (IntervalKind.Away, null, At(12), At(20)),
        ]);
    }

    [Fact]
    public void Idle_under_the_threshold_has_no_effect()
    {
        Send(new TrackingStarted(At(0), Outlook()));

        Tick(at: 4.9, lastInput: 0).ShouldBeEmpty();
        Send(new TrackingStopped(At(4.9)));

        _timeline.Intervals.ShouldHaveSingleItem().Kind.ShouldBe(IntervalKind.Active);
    }

    [Fact]
    public void Input_after_being_away_resumes_tracking_for_the_window_in_front()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        Tick(at: 10, lastInput: 5);              // away since 5
        Send(new ForegroundChanged(At(30), Code()));
        Tick(at: 60, lastInput: 59.5);           // back at 59.5, Code in front

        Send(new TrackingStopped(At(70)));

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "olk", At(0), At(5)),
            (IntervalKind.Away, null, At(5), At(59.5)),
            (IntervalKind.Active, "code", At(59.5), At(70)),
        ]);
    }

    [Fact]
    public void Foreground_changes_while_away_do_not_create_intervals()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        Tick(at: 10, lastInput: 5);

        Send(new ForegroundChanged(At(11), Teams())).ShouldBeEmpty();
        Send(new ForegroundChanged(At(12), Browser())).ShouldBeEmpty();
        Tick(at: 13, lastInput: 5);

        _timeline.Intervals.Count.ShouldBe(2);
        _engine.Current!.Kind.ShouldBe(IntervalKind.Away);
    }

    [Fact]
    public void A_window_change_without_input_before_going_idle_is_dropped()
    {
        // A notification pops up at minute 8; the user left at minute 2.
        Send(new TrackingStarted(At(0), Outlook()));
        Send(new ForegroundChanged(At(8), Teams()));
        Tick(at: 8.5, lastInput: 2);

        Send(new TrackingStopped(At(9)));

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "olk", At(0), At(2)),
            (IntervalKind.Away, null, At(2), At(9)),
        ]);
    }

    [Fact]
    public void A_window_change_the_user_made_before_leaving_is_kept()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        Send(new ForegroundChanged(At(1), Code()));
        Tick(at: 7, lastInput: 2); // switched at 1, kept typing until 2, then left

        Send(new TrackingStopped(At(8)));

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "olk", At(0), At(1)),
            (IntervalKind.Active, "code", At(1), At(2)),
            (IntervalKind.Away, null, At(2), At(8)),
        ]);
    }

    [Fact]
    public void Away_never_starts_before_the_current_interval()
    {
        // Code came to the front at minute 10 without input; last input was at minute 6.
        Send(new TrackingStarted(At(0), Outlook()));
        Send(new ForegroundChanged(At(10), Code()));
        Tick(at: 10.5, lastInput: 6); // not idle yet: commits Code at 10
        Tick(at: 12, lastInput: 6);   // idle: away from max(6, 10)

        _timeline.Intervals.Select(i => i.Start).ShouldBe([At(0), At(10), At(10)]);
        _timeline.Intervals[0].End.ShouldBe(At(10));
        _timeline.Intervals.ShouldAllBe(i => i.End >= i.Start);
    }

    [Fact]
    public void An_idle_exempt_app_keeps_counting_without_input()
    {
        _engine.Options = _engine.Options.WithIdleExemptApps("MS-TEAMS");
        Send(new TrackingStarted(At(0), Teams("Daily standup")));

        Tick(at: 30, lastInput: 0);
        Send(new TrackingStopped(At(30)));

        var interval = _timeline.Intervals.ShouldHaveSingleItem();
        interval.Kind.ShouldBe(IntervalKind.Active);
        interval.Duration.ShouldBe(TimeSpan.FromMinutes(30));
    }

    [Fact]
    public void Idle_detection_can_be_turned_off()
    {
        _engine.Options = _engine.Options with { IdleThreshold = null };
        Send(new TrackingStarted(At(0), Outlook()));

        Tick(at: 120, lastInput: 0).ShouldAllBe(c => c.Interval.Kind == IntervalKind.Active);
    }

    [Fact]
    public void A_threshold_change_takes_effect_on_the_next_tick()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        Tick(at: 3, lastInput: 0).ShouldBeEmpty();

        _engine.Options = _engine.Options with { IdleThreshold = TimeSpan.FromMinutes(2) };

        Tick(at: 3.1, lastInput: 0).ShouldContain(c => c.Interval.Kind == IntervalKind.Away);
    }

    [Fact]
    public void Last_input_in_the_future_is_treated_as_now()
    {
        Send(new TrackingStarted(At(0), Outlook()));

        Tick(at: 10, lastInput: 15).ShouldAllBe(c => c.Interval.Kind == IntervalKind.Active);
        _engine.Current!.Kind.ShouldBe(IntervalKind.Active);
    }

    [Fact]
    public void Stop_while_away_closes_the_away_interval()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        Tick(at: 10, lastInput: 1);
        Send(new TrackingStopped(At(20)));

        _timeline.Intervals.ShouldAllBe(i => !i.IsOpen);
        _timeline.Intervals[^1].Kind.ShouldBe(IntervalKind.Away);
        _timeline.Intervals[^1].End.ShouldBe(At(20));
    }

    [Fact]
    public void Default_threshold_is_five_minutes()
    {
        TrackingOptions.Default.IdleThreshold.ShouldBe(Threshold);
    }
}
