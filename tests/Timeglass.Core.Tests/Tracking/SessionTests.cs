using Timeglass.Core.Domain;
using Timeglass.Core.Ports;
using Timeglass.Core.Tracking;
using Timeglass.Testing;
using static Timeglass.Testing.TestWindows;

namespace Timeglass.Core.Tests.Tracking;

public class SessionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private readonly TrackingEngine _engine = new(
        new ExeNameAppIdentityResolver(),
        TrackingOptions.Default with
        {
            IdleThreshold = TimeSpan.FromMinutes(5),
            CheckpointInterval = TimeSpan.FromDays(1), // keep checkpoints out of these tests
            ClockJumpThreshold = TimeSpan.FromMinutes(2),
        });

    private readonly IntervalTimeline _timeline = new();

    private static DateTimeOffset At(double minutes) => T0.AddMinutes(minutes);

    private IReadOnlyList<IntervalChange> Send(TrackingEvent e)
    {
        var changes = _engine.Handle(e);
        _timeline.Apply(changes);
        return changes;
    }

    private IReadOnlyList<IntervalChange> Session(double at, SessionEvent sessionEvent, WindowSnapshot? window = null) =>
        Send(new SessionChanged(At(at), sessionEvent, window));

    /// <summary>Ticks every 30 seconds from <paramref name="from"/> to <paramref name="to"/>, with input at each tick.</summary>
    private void TickActive(double from, double to)
    {
        for (var at = from; at <= to; at += 0.5)
        {
            Send(new Tick(At(at), At(at)));
        }
    }

    /// <summary>Ticks every 30 seconds with the last input fixed at <paramref name="lastInput"/>.</summary>
    private void TickWithInputAt(double from, double to, double lastInput)
    {
        for (var at = from; at <= to; at += 0.5)
        {
            Send(new Tick(At(at), At(lastInput)));
        }
    }

    private IReadOnlyList<(IntervalKind Kind, string? App, DateTimeOffset Start, DateTimeOffset End)> Summary() =>
        [.. _timeline.Intervals.Select(i => (i.Kind, i.App?.Key, i.Start, i.End))];

    [Fact]
    public void Lock_then_unlock_produces_active_away_active_with_exact_boundaries()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        TickActive(0.5, 10);
        Session(10.2, SessionEvent.Locked);
        TickActive(10.5, 20); // input on the lock screen does not count
        Session(20.1, SessionEvent.Unlocked, Code());
        TickActive(20.5, 25);

        Send(new TrackingStopped(At(25)));

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "olk", At(0), At(10.2)),
            (IntervalKind.Away, null, At(10.2), At(20.1)),
            (IntervalKind.Active, "code", At(20.1), At(25)),
        ]);
    }

    [Fact]
    public void Lock_keeps_a_switch_made_just_before_it()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        TickActive(0.5, 4.5);
        Send(new ForegroundChanged(At(5), Code()));
        Session(5.5, SessionEvent.Locked);
        Session(8, SessionEvent.Unlocked, Code());
        Send(new TrackingStopped(At(9)));

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "olk", At(0), At(5)),
            (IntervalKind.Active, "code", At(5), At(5.5)),
            (IntervalKind.Away, null, At(5.5), At(8)),
            (IntervalKind.Active, "code", At(8), At(9)),
        ]);
    }

    [Fact]
    public void Lock_while_idle_extends_the_existing_away_interval()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        TickWithInputAt(0.5, 8, lastInput: 2); // away since 2
        Session(8.1, SessionEvent.Locked).ShouldBeEmpty();
        TickActive(8.5, 9);
        Session(9.2, SessionEvent.Unlocked, Outlook());
        Send(new TrackingStopped(At(10)));

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "olk", At(0), At(2)),
            (IntervalKind.Away, null, At(2), At(9.2)),
            (IntervalKind.Active, "olk", At(9.2), At(10)),
        ]);
    }

    [Fact]
    public void Suspend_and_resume_behave_like_lock_and_unlock()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        TickActive(0.5, 30);
        Session(30, SessionEvent.Suspending);
        Session(90, SessionEvent.Resumed, Browser()); // no ticks while asleep
        TickActive(90.5, 95);
        Send(new TrackingStopped(At(95)));

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "olk", At(0), At(30)),
            (IntervalKind.Away, null, At(30), At(90)),
            (IntervalKind.Active, "firefox", At(90), At(95)),
        ]);
    }

    [Fact]
    public void Resume_into_a_locked_session_stays_away_until_unlock()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        TickActive(0.5, 10);
        Session(10, SessionEvent.Locked);
        Session(10.1, SessionEvent.Suspending);
        Session(60, SessionEvent.Resumed, Outlook()).ShouldBeEmpty();
        TickActive(60.5, 62);
        Session(62.2, SessionEvent.Unlocked, Outlook());
        Send(new TrackingStopped(At(63)));

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "olk", At(0), At(10)),
            (IntervalKind.Away, null, At(10), At(62.2)),
            (IntervalKind.Active, "olk", At(62.2), At(63)),
        ]);
    }

    [Fact]
    public void Sleep_without_a_suspend_event_is_caught_by_the_clock_jump_rule()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        TickActive(0.5, 30);

        // Asleep from 30 to 90; the suspend event was missed. First tick after waking up:
        // the user is not typing yet.
        Send(new Tick(At(90), At(30)));
        TickActive(90.5, 95);
        Send(new TrackingStopped(At(95)));

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "olk", At(0), At(30)),
            (IntervalKind.Away, null, At(30), At(90.5)),
            (IntervalKind.Active, "olk", At(90.5), At(95)),
        ]);
    }

    [Fact]
    public void A_clock_jump_is_away_even_for_idle_exempt_apps()
    {
        _engine.Options = _engine.Options.WithIdleExemptApps("ms-teams");
        Send(new TrackingStarted(At(0), Teams()));
        TickWithInputAt(0.5, 10, lastInput: 0);

        Send(new Tick(At(70), At(70))); // clock set forward an hour
        Send(new Tick(At(70.5), At(70.5)));
        Send(new TrackingStopped(At(71)));

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "ms-teams", At(0), At(10)),
            (IntervalKind.Away, null, At(10), At(70.5)),
            (IntervalKind.Active, "ms-teams", At(70.5), At(71)),
        ]);
    }

    [Fact]
    public void Input_from_before_the_jump_does_not_end_the_away_interval()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        TickActive(0.5, 10);

        Send(new Tick(At(60), At(10)));
        Send(new Tick(At(61), At(10))).ShouldBeEmpty();

        _engine.Current!.Kind.ShouldBe(IntervalKind.Away);
        _engine.Current.Start.ShouldBe(At(10));
    }

    [Fact]
    public void Gaps_under_the_jump_threshold_are_attributed_normally()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        Send(new Tick(At(1.9), At(1.9))).ShouldBeEmpty();

        _engine.Current!.Kind.ShouldBe(IntervalKind.Active);
    }

    [Fact]
    public void Clock_jump_detection_can_be_turned_off()
    {
        _engine.Options = _engine.Options with { ClockJumpThreshold = null };
        Send(new TrackingStarted(At(0), Outlook()));

        Send(new Tick(At(3), At(3))).ShouldBeEmpty();
        _engine.Current!.Kind.ShouldBe(IntervalKind.Active);
    }

    [Fact]
    public void Shutdown_leaves_no_open_interval()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        Send(new ForegroundChanged(At(1), Code()));
        TickActive(1.5, 2);

        Session(2.2, SessionEvent.ShuttingDown);

        _timeline.Intervals.ShouldAllBe(i => !i.IsOpen);
        _timeline.Intervals[^1].End.ShouldBe(At(2.2));
        _engine.IsTracking.ShouldBeFalse();
        _engine.Current.ShouldBeNull();
    }

    [Fact]
    public void Shutdown_while_locked_closes_the_away_interval()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        Session(1, SessionEvent.Locked);
        Session(5, SessionEvent.ShuttingDown);

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "olk", At(0), At(1)),
            (IntervalKind.Away, null, At(1), At(5)),
        ]);
        _timeline.Intervals.ShouldAllBe(i => !i.IsOpen);
    }

    [Fact]
    public void Ticks_after_shutdown_are_ignored_but_activity_restarts_a_cancelled_shutdown()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        Session(1, SessionEvent.ShuttingDown);

        Send(new Tick(At(1.5), At(1.5))).ShouldBeEmpty();
        Send(new ForegroundChanged(At(1.6), Code())).ShouldHaveSingleItem().Kind.ShouldBe(IntervalChangeKind.Opened);
        Send(new TrackingStopped(At(2)));

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "olk", At(0), At(1)),
            (IntervalKind.Active, "code", At(1.6), At(2)),
        ]);
    }

    [Fact]
    public void Unlock_without_a_lock_is_treated_as_a_foreground_change()
    {
        Send(new TrackingStarted(At(0), Outlook()));
        Session(1, SessionEvent.Unlocked, Code());
        TickActive(1.5, 2);
        Send(new TrackingStopped(At(2)));

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "olk", At(0), At(1)),
            (IntervalKind.Active, "code", At(1), At(2)),
        ]);
    }
}
