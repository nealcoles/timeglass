using Timeglass.Core.Apps;
using Timeglass.Core.Domain;
using Timeglass.Core.Ports;
using Timeglass.Core.Tracking;
using Timeglass.Testing;
using static Timeglass.Testing.TestWindows;

namespace Timeglass.Core.Tests.Tracking;

/// <summary>
/// Pausing stops the run and resuming starts a new one, so a pause is a
/// <see cref="TrackingStopped"/> followed by a <see cref="TrackingStarted"/>.
/// </summary>
public class PauseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private readonly TrackingEngine _engine = new(
        new AliasAppIdentityResolver(AppAliasTable.Empty),
        TrackingOptions.Default with
        {
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

    private IReadOnlyList<IntervalChange> Pause(double at) => Send(new TrackingStopped(At(at)));

    private void Resume(double at, WindowSnapshot? window) => Send(new TrackingStarted(At(at), window));

    private IReadOnlyList<(IntervalKind Kind, string? App, DateTimeOffset Start, DateTimeOffset End)> Summary() =>
        [.. _timeline.Intervals.Select(i => (i.Kind, i.App?.Key, i.Start, i.End))];

    [Fact]
    public void Paused_time_is_a_gap_between_the_interval_before_and_the_one_after()
    {
        Resume(at: 0, Code());
        Pause(at: 10);
        Resume(at: 40, Outlook());
        Pause(at: 50);

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "code", At(0), At(10)),
            (IntervalKind.Active, "olk", At(40), At(50)),
        ]);
        _timeline.Intervals.ShouldAllBe(i => !i.IsOpen);
    }

    [Fact]
    public void Events_while_paused_record_nothing()
    {
        Resume(at: 0, Code());
        Pause(at: 10);

        Send(new ForegroundChanged(At(11), Outlook())).ShouldBeEmpty();
        Send(new Tick(At(12), At(12))).ShouldBeEmpty();
        Send(new SessionChanged(At(13), SessionEvent.Locked)).ShouldBeEmpty();
        Send(new SessionChanged(At(14), SessionEvent.Unlocked, Outlook())).ShouldBeEmpty();
        _engine.IsTracking.ShouldBeFalse();
        _engine.Current.ShouldBeNull();
    }

    [Fact]
    public void Resuming_after_a_long_pause_is_not_a_clock_jump()
    {
        _engine.Options = _engine.Options with { ClockJumpThreshold = TimeSpan.FromMinutes(2) };
        Resume(at: 0, Code());
        Pause(at: 1);
        Resume(at: 70, Code());
        Send(new Tick(At(70.02), At(70.02)));

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "code", At(0), At(1)),
            (IntervalKind.Active, "code", At(70), At(70)),
        ]);
    }

    [Fact]
    public void A_switch_made_before_pausing_counts_up_to_the_pause()
    {
        Resume(at: 0, Code());
        Send(new ForegroundChanged(At(5), Outlook()));
        Pause(at: 10);

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "code", At(0), At(5)),
            (IntervalKind.Active, "olk", At(5), At(10)),
        ]);
    }

    [Fact]
    public void Pausing_while_away_ends_the_away_interval_at_the_pause()
    {
        Resume(at: 0, Code());
        Send(new SessionChanged(At(5), SessionEvent.Locked));
        Pause(at: 10);
        Resume(at: 20, Code());

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "code", At(0), At(5)),
            (IntervalKind.Away, null, At(5), At(10)),
            (IntervalKind.Active, "code", At(20), At(20)),
        ]);
    }

    [Fact]
    public void Resuming_does_not_carry_over_a_lock_from_before_the_pause()
    {
        Resume(at: 0, Code());
        Send(new SessionChanged(At(5), SessionEvent.Locked));
        Pause(at: 10);
        Resume(at: 20, Code());
        Send(new Tick(At(20.5), At(20.5)));

        _engine.Current.ShouldNotBeNull().Kind.ShouldBe(IntervalKind.Active);
    }

    [Fact]
    public void Pausing_twice_or_resuming_twice_is_harmless()
    {
        Resume(at: 0, Code());
        Resume(at: 5, Code());
        Pause(at: 10);
        Pause(at: 11).ShouldBeEmpty();

        Summary().ShouldBe(
        [
            (IntervalKind.Active, "code", At(0), At(5)),
            (IntervalKind.Active, "code", At(5), At(10)),
        ]);
    }
}

public class PauseScheduleTests
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    private static DateTimeOffset Local(int year, int month, int day, int hour, int minute, int offsetHours) =>
        new(year, month, day, hour, minute, 0, TimeSpan.FromHours(offsetHours));

    [Fact]
    public void Until_resumed_has_no_end()
    {
        PauseSchedule.EndOf(PauseLength.UntilResumed, Local(2026, 11, 5, 14, 0, 1), Oslo).ShouldBeNull();
    }

    [Theory]
    [InlineData(PauseLength.ThirtyMinutes, 30)]
    [InlineData(PauseLength.OneHour, 60)]
    public void Fixed_lengths_end_that_long_after_now(PauseLength length, int minutes)
    {
        var now = Local(2026, 11, 5, 14, 10, 1);

        PauseSchedule.EndOf(length, now, Oslo).ShouldBe(now.AddMinutes(minutes));
    }

    [Fact]
    public void A_fixed_length_spanning_a_clock_change_is_still_real_time()
    {
        var now = Local(2026, 10, 25, 2, 45, 2); // 02:45 summer time, the clocks go back at 03:00

        PauseSchedule.EndOf(PauseLength.OneHour, now, Oslo).ShouldBe(now.AddHours(1));
    }

    [Fact]
    public void Until_tomorrow_ends_at_the_next_local_midnight()
    {
        var end = PauseSchedule.EndOf(PauseLength.UntilTomorrow, Local(2026, 11, 5, 14, 10, 1), Oslo);

        end.ShouldBe(Local(2026, 11, 6, 0, 0, 1));
    }

    [Fact]
    public void Until_tomorrow_just_before_midnight_ends_minutes_later()
    {
        var end = PauseSchedule.EndOf(PauseLength.UntilTomorrow, Local(2026, 11, 5, 23, 59, 1), Oslo);

        end.ShouldBe(Local(2026, 11, 6, 0, 0, 1));
    }

    [Fact]
    public void Until_tomorrow_uses_the_local_day_not_the_utc_day()
    {
        // 00:30 local on the 6th is still the 5th in UTC.
        var now = Local(2026, 11, 6, 0, 30, 1);

        PauseSchedule.EndOf(PauseLength.UntilTomorrow, now.ToUniversalTime(), Oslo).ShouldBe(Local(2026, 11, 7, 0, 0, 1));
    }

    [Fact]
    public void Until_tomorrow_on_the_day_the_clocks_go_forward_has_the_new_offset()
    {
        // Oslo switches to summer time on 29 March 2026; the next midnight is at +02:00.
        var end = PauseSchedule.EndOf(PauseLength.UntilTomorrow, Local(2026, 3, 29, 12, 0, 2), Oslo);

        end.ShouldBe(Local(2026, 3, 30, 0, 0, 2));
    }

    [Fact]
    public void Until_tomorrow_where_midnight_is_skipped_ends_when_the_day_starts()
    {
        // A zone that springs forward at midnight: 00:00 to 01:00 does not exist.
        var zone = TimeZoneInfo.CreateCustomTimeZone(
            "Test/MidnightGap",
            TimeSpan.Zero,
            "Midnight gap",
            "Standard",
            "Daylight",
            [
                TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
                    new DateTime(2000, 1, 1),
                    new DateTime(2099, 12, 31),
                    TimeSpan.FromHours(1),
                    TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 4, 1),
                    TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 10, 1)),
            ]);

        var end = PauseSchedule.EndOf(PauseLength.UntilTomorrow, new DateTimeOffset(2026, 3, 31, 12, 0, 0, TimeSpan.Zero), zone);

        end.ShouldBe(new DateTimeOffset(2026, 4, 1, 1, 0, 0, TimeSpan.FromHours(1)));
    }
}
