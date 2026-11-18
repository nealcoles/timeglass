using Timeglass.Core.Domain;
using Timeglass.Core.Reporting;
using static Timeglass.Core.Tests.Reporting.ReportingData;

namespace Timeglass.Core.Tests.Reporting;

public class RangeSummaryTests
{
    // Monday 2 November to Sunday 8 November 2026, Oslo (UTC+1).
    private static readonly DateOnly Monday = Day("2026-11-02");
    private static readonly DateOnly Sunday = Day("2026-11-08");

    private static string Describe(DayTotal day) =>
        $"{day.Day:ddd} {day.Active.TotalMinutes}/{day.Away.TotalMinutes}"
        + string.Concat(day.Apps.Select(a => $" {a.App.Key}:{a.Duration.TotalMinutes}"));

    public static TheoryData<string, ActivityInterval[], string[]> Weeks => new()
    {
        {
            "an empty week has every day",
            [],
            ["Mon 0/0", "Tue 0/0", "Wed 0/0", "Thu 0/0", "Fri 0/0", "Sat 0/0", "Sun 0/0"]
        },
        {
            "every app per day, longest first",
            [
                Active(Utc("2026-11-02T08:00Z"), Utc("2026-11-02T10:00Z"), Code),
                Active(Utc("2026-11-02T10:00Z"), Utc("2026-11-02T10:30Z"), Teams),
                Active(Utc("2026-11-02T10:30Z"), Utc("2026-11-02T11:30Z"), Outlook),
                Away(Utc("2026-11-02T11:30Z"), Utc("2026-11-02T12:00Z")),
                Active(Utc("2026-11-04T08:00Z"), Utc("2026-11-04T08:20Z"), Teams),
            ],
            ["Mon 210/30 code:120 outlook:60 teams:30", "Tue 0/0", "Wed 20/0 teams:20", "Thu 0/0", "Fri 0/0", "Sat 0/0", "Sun 0/0"]
        },
        {
            "an interval over midnight counts towards both days",
            [Active(Utc("2026-11-03T22:00Z"), Utc("2026-11-04T01:00Z"), Code)],
            ["Mon 0/0", "Tue 60/0 code:60", "Wed 120/0 code:120", "Thu 0/0", "Fri 0/0", "Sat 0/0", "Sun 0/0"]
        },
        {
            "time outside the week is left out",
            [
                Active(Utc("2026-11-01T22:00Z"), Utc("2026-11-02T00:00Z"), Code), // 23:00 Sunday to 01:00 Monday
                Active(Utc("2026-11-08T22:30Z"), Utc("2026-11-09T00:00Z"), Outlook), // 23:30 Sunday to 01:00 Monday
            ],
            ["Mon 60/0 code:60", "Tue 0/0", "Wed 0/0", "Thu 0/0", "Fri 0/0", "Sat 0/0", "Sun 30/0 outlook:30"]
        },
    };

    [Theory]
    [MemberData(nameof(Weeks))]
    public void Summarises_each_day_of_a_week(string because, ActivityInterval[] intervals, string[] expected)
    {
        var summary = RangeSummaries.Build(intervals, Monday, Sunday, Oslo, Later);

        summary.Days.Select(Describe).ShouldBe(expected, because);
        summary.Active.ShouldBe(new TimeSpan(summary.Days.Sum(d => d.Active.Ticks)));
        summary.Away.ShouldBe(new TimeSpan(summary.Days.Sum(d => d.Away.Ticks)));
        summary.Apps.Sum(a => a.Duration.Ticks).ShouldBe(summary.Active.Ticks);
        summary.Apps.Sum(a => a.Percent).ShouldBe(summary.Active > TimeSpan.Zero ? 100 : 0);
    }

    [Fact]
    public void Each_day_matches_its_day_summary()
    {
        var random = new Random(26);
        var intervals = new List<ActivityInterval>();
        var at = Utc("2026-10-20T00:00Z");
        AppIdentity?[] apps = [Code, Outlook, Teams, null];
        while (at < Utc("2026-11-05T00:00Z"))
        {
            var start = at.AddMinutes(random.Next(0, 3) == 0 ? random.Next(0, 120) : 0);
            var end = start.AddMinutes(random.Next(1, 300));
            var app = apps[random.Next(apps.Length)];
            intervals.Add(app is null ? Away(start, end) : Active(start, end, app));
            at = end;
        }

        // Includes the 25 hour day, 25 October, and a day start after midnight.
        var dayStart = TimeSpan.FromHours(4);
        var summary = RangeSummaries.Build(intervals, Day("2026-10-21"), Day("2026-11-03"), Oslo, Later, dayStart);

        summary.Days.Count.ShouldBe(14);
        foreach (var day in summary.Days)
        {
            var expected = DaySummaries.Build(intervals, day.Day, Oslo, Later, dayStart);
            (day.Start, day.End, day.Active, day.Away).ShouldBe((expected.Start, expected.End, expected.Active, expected.Away));
            day.Apps.ShouldBe(expected.Apps);
        }
    }

    [Fact]
    public void App_totals_cover_the_whole_range_with_the_latest_name()
    {
        var renamed = Code with { DisplayName = "VS Code" };
        ActivityInterval[] intervals =
        [
            Active(Utc("2026-11-02T08:00Z"), Utc("2026-11-02T09:00Z"), Code),
            Active(Utc("2026-11-03T08:00Z"), Utc("2026-11-03T09:30Z"), Outlook),
            Active(Utc("2026-11-04T08:00Z"), Utc("2026-11-04T09:00Z"), renamed),
            Active(Utc("2026-11-05T08:00Z"), Utc("2026-11-05T08:30Z"), Teams),
        ];

        var summary = RangeSummaries.Build(intervals, Monday, Sunday, Oslo, Later);

        summary.Apps.Select(a => (a.App.DisplayName, a.Duration.TotalMinutes, a.Percent)).ShouldBe(
        [
            ("VS Code", 120, 50),
            ("Outlook", 90, 38),
            ("Teams", 30, 12),
        ]);
    }

    [Fact]
    public void An_open_interval_runs_until_now_and_not_into_later_days()
    {
        var open = Active(Utc("2026-11-04T08:00Z"), Utc("2026-11-04T08:10Z"), Code, open: true);

        var summary = RangeSummaries.Build([open], Monday, Sunday, Oslo, Utc("2026-11-04T10:00Z"));

        summary.Days.Select(d => d.Active.TotalMinutes).ShouldBe([0, 0, 120, 0, 0, 0, 0]);
    }

    [Fact]
    public void Range_bounds_follow_the_local_days()
    {
        var (start, end) = RangeSummaries.Bounds(Day("2026-10-19"), Day("2026-10-25"), Oslo);

        start.ShouldBe(Utc("2026-10-18T22:00Z"));
        end.ShouldBe(Utc("2026-10-25T23:00Z"));
        (end - start).ShouldBe(TimeSpan.FromHours(7 * 24 + 1));
    }

    [Fact]
    public void Arguments_are_checked()
    {
        Should.Throw<ArgumentException>(() => RangeSummaries.Build([], Sunday, Monday, Oslo, Later));
        Should.Throw<ArgumentException>(() => RangeSummaries.Build([], Day("2026-01-01"), Day("2027-01-02"), Oslo, Later)); // 367 days
        RangeSummaries.Build([], Day("2026-01-01"), Day("2026-12-31"), Oslo, Later).Days.Count.ShouldBe(365);
    }
}
