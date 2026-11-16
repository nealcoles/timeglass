using System.Globalization;
using Timeglass.Core.Domain;
using Timeglass.Core.Reporting;

namespace Timeglass.Core.Tests.Reporting;

public sealed class DailyTotalsTests
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
    private static readonly AppIdentity Code = new("code", "Code");
    private static readonly AppIdentity Outlook = new("outlook", "Outlook");

    private static DateTimeOffset Utc(string at) => DateTimeOffset.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static DateOnly Day(string day) => DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static ActivityInterval Active(string start, string end, AppIdentity? app, string? title = "t") =>
        new(Guid.CreateVersion7(), IntervalKind.Active, Utc(start), Utc(end), false, app, null, title);

    private static ActivityInterval Away(string start, string end) =>
        new(Guid.CreateVersion7(), IntervalKind.Away, Utc(start), Utc(end), false, null, null, null);

    // 24 to 26 October 2026 in Oslo, over the end of summer time (25 October is 25 hours long),
    // with an evening that runs past midnight on both nights.
    private static readonly ActivityInterval[] Intervals =
    [
        Active("2026-10-24T07:00Z", "2026-10-24T08:00Z", Code, "a.cs"),
        Active("2026-10-24T08:00Z", "2026-10-24T08:30Z", Code, "b.cs"),
        Away("2026-10-24T08:30Z", "2026-10-24T09:00Z"),
        Active("2026-10-24T09:00Z", "2026-10-24T09:20Z", null),
        Active("2026-10-24T21:00Z", "2026-10-24T23:30Z", Outlook, "Inbox"), // 23:00 to 01:30 local
        Active("2026-10-25T22:30Z", "2026-10-26T00:00Z", Code, "late.cs"), // 23:30 to 01:00 local
        Active("2026-10-26T08:00Z", "2026-10-26T09:00Z", Outlook, "Inbox"),
    ];

    [Theory]
    [InlineData("2026-10-24")]
    [InlineData("2026-10-25")]
    [InlineData("2026-10-26")]
    public void A_day_summary_from_the_totals_equals_one_from_the_intervals(string day)
    {
        var date = Day(day);
        var before = Utc("2026-10-27T00:00Z");
        var now = Utc("2026-11-01T00:00Z");

        var totals = DailyTotals.Build(Intervals, before, Oslo);
        var fromIntervals = DaySummaries.Build(Intervals, date, Oslo, now);
        var fromTotals = DaySummaries.Build([], date, Oslo, now, rolledUp: totals);

        fromTotals.ShouldBeEquivalentTo(fromIntervals);
    }

    [Fact]
    public void The_totals_add_up_to_the_intervals_they_replace()
    {
        var totals = DailyTotals.Build(Intervals, Utc("2026-10-27T00:00Z"), Oslo);

        totals.Sum(t => t.Duration.Ticks).ShouldBe(Intervals.Sum(i => i.Duration.Ticks));
        foreach (var kind in new[] { IntervalKind.Active, IntervalKind.Away })
        {
            totals.Where(t => t.Kind == kind).Sum(t => t.Duration.Ticks)
                .ShouldBe(Intervals.Where(i => i.Kind == kind).Sum(i => i.Duration.Ticks));
        }
    }

    [Fact]
    public void An_interval_crossing_midnight_counts_towards_both_days()
    {
        var totals = DailyTotals.Build(Intervals, Utc("2026-10-27T00:00Z"), Oslo);

        totals.Where(t => t.App == Outlook).Select(t => (t.Day, t.Duration)).ShouldBe(
        [
            (Day("2026-10-24"), TimeSpan.FromHours(1)),
            (Day("2026-10-25"), TimeSpan.FromMinutes(90)),
            (Day("2026-10-26"), TimeSpan.FromHours(1)),
        ]);
    }

    [Fact]
    public void Only_time_before_the_cut_off_is_counted()
    {
        // 09:30 local on the 24th, in the middle of the first interval.
        var totals = DailyTotals.Build(Intervals, Utc("2026-10-24T07:30Z"), Oslo);

        totals.ShouldHaveSingleItem().ShouldBe(new RolledUpTotal(
            Day("2026-10-24"), IntervalKind.Active, Code, TimeSpan.FromMinutes(30), Utc("2026-10-24T07:00Z"), Utc("2026-10-24T07:30Z")));
    }

    [Fact]
    public void Away_time_and_time_without_an_app_have_no_app()
    {
        var totals = DailyTotals.Build(Intervals, Utc("2026-10-24T10:00Z"), Oslo);

        totals.Select(t => (t.Kind, t.App?.Key, t.Duration)).ShouldBe(
        [
            (IntervalKind.Active, null, TimeSpan.FromMinutes(20)),
            (IntervalKind.Active, "code", TimeSpan.FromMinutes(90)),
            (IntervalKind.Away, null, TimeSpan.FromMinutes(30)),
        ]);
    }
}
