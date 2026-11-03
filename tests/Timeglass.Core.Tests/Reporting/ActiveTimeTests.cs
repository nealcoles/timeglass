using Timeglass.Core.Domain;
using Timeglass.Core.Reporting;

namespace Timeglass.Core.Tests.Reporting;

public class ActiveTimeTests
{
    private static readonly DateTimeOffset From = new(2026, 11, 3, 0, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset To = From.AddHours(12);

    private static ActivityInterval Interval(
        double startHours, double endHours, IntervalKind kind = IntervalKind.Active, bool isOpen = false) =>
        new(
            Guid.NewGuid(),
            kind,
            From.AddHours(startHours),
            From.AddHours(endHours),
            isOpen,
            kind == IntervalKind.Active ? new AppIdentity("code", "Code") : null,
            null,
            null);

    [Fact]
    public void Sums_active_intervals_and_skips_away_ones()
    {
        var total = ActiveTime.Total(
            [Interval(1, 2), Interval(2, 3, IntervalKind.Away), Interval(3, 3.5)],
            From,
            To);

        total.ShouldBe(TimeSpan.FromHours(1.5));
    }

    [Fact]
    public void Clips_intervals_to_the_range()
    {
        var total = ActiveTime.Total([Interval(-2, 1), Interval(11, 14)], From, To);

        total.ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Counts_an_open_interval_until_the_end_of_the_range()
    {
        // End is the last checkpoint; the interval is still running.
        var total = ActiveTime.Total([Interval(10, 10.5, isOpen: true)], From, To);

        total.ShouldBe(TimeSpan.FromHours(2));
    }

    [Fact]
    public void Ignores_intervals_outside_the_range()
    {
        var total = ActiveTime.Total([Interval(-3, -1), Interval(13, 14)], From, To);

        total.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Start_of_day_is_local_midnight()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("UTC+2", TimeSpan.FromHours(2), "UTC+2", "UTC+2");

        var start = ActiveTime.StartOfDay(new DateTimeOffset(2026, 11, 3, 23, 30, 0, TimeSpan.Zero), zone);

        start.ShouldBe(new DateTimeOffset(2026, 11, 4, 0, 0, 0, TimeSpan.FromHours(2)));
    }

    [Fact]
    public void Start_of_day_skips_a_midnight_that_does_not_exist()
    {
        // Clocks jump from 00:00 to 01:00 on 2026-03-29.
        var zone = ZoneWithMidnightTransitions();

        var start = ActiveTime.StartOfDay(new DateTimeOffset(2026, 3, 29, 12, 0, 0, TimeSpan.FromHours(1)), zone);

        start.ShouldBe(new DateTimeOffset(2026, 3, 29, 1, 0, 0, TimeSpan.FromHours(1)));
    }

    [Fact]
    public void Start_of_day_takes_the_first_of_two_midnights()
    {
        // Clocks go back from 01:00 to 00:00 on 2026-10-25, so midnight happens twice.
        var zone = ZoneWithMidnightTransitions();

        var start = ActiveTime.StartOfDay(new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero), zone);

        start.ShouldBe(new DateTimeOffset(2026, 10, 25, 0, 0, 0, TimeSpan.FromHours(1)));
    }

    /// <summary>UTC+0 in winter, UTC+1 in summer, with both changes at midnight local time.</summary>
    private static TimeZoneInfo ZoneWithMidnightTransitions()
    {
        var rule = TimeZoneInfo.AdjustmentRule.CreateAdjustmentRule(
            new DateTime(2026, 1, 1),
            new DateTime(2026, 12, 31),
            TimeSpan.FromHours(1),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 0, 0, 0), 3, 29),
            TimeZoneInfo.TransitionTime.CreateFixedDateRule(new DateTime(1, 1, 1, 1, 0, 0), 10, 25));

        return TimeZoneInfo.CreateCustomTimeZone("Midnight DST", TimeSpan.Zero, "Midnight DST", "Winter", "Summer", [rule]);
    }
}
