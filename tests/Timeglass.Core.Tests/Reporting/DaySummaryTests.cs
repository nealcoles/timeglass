using System.Globalization;
using Timeglass.Core.Domain;
using Timeglass.Core.Reporting;

namespace Timeglass.Core.Tests.Reporting;

public class DaySummaryTests
{
    // Oslo is UTC+1 in winter and UTC+2 in summer. In 2026 the clocks go forward at 02:00 on
    // 29 March (a 23 hour day) and back at 03:00 on 25 October (a 25 hour day).
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");

    private static readonly AppIdentity Code = new("code", "Code");
    private static readonly AppIdentity Outlook = new("outlook", "Outlook");
    private static readonly AppIdentity Teams = new("teams", "Teams");

    private static readonly DateTimeOffset Later = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private static DateOnly Day(string day) => DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static DateTimeOffset Utc(string at) => DateTimeOffset.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static TimeSpan Clock(string time) => TimeSpan.ParseExact(time, @"hh\:mm", CultureInfo.InvariantCulture);

    private static ActivityInterval Active(DateTimeOffset start, DateTimeOffset end, AppIdentity? app = null, bool open = false) =>
        new(Guid.NewGuid(), IntervalKind.Active, start, end, open, app ?? Code, null, null);

    private static ActivityInterval Away(DateTimeOffset start, DateTimeOffset end, bool open = false) =>
        new(Guid.NewGuid(), IntervalKind.Away, start, end, open, null, null, null);

    [Theory]
    [InlineData("2026-11-05", "00:00", "2026-11-04T23:00Z", 24)]
    [InlineData("2026-07-01", "00:00", "2026-06-30T22:00Z", 24)]
    [InlineData("2026-03-29", "00:00", "2026-03-28T23:00Z", 23)] // spring forward
    [InlineData("2026-10-25", "00:00", "2026-10-24T22:00Z", 25)] // fall back
    [InlineData("2026-11-05", "04:00", "2026-11-05T03:00Z", 24)]
    [InlineData("2026-03-28", "02:30", "2026-03-28T01:30Z", 23.5)] // ends when the clocks jump past 02:30
    [InlineData("2026-03-29", "02:30", "2026-03-29T01:00Z", 23.5)] // 02:30 doesn't exist: starts at the jump
    [InlineData("2026-10-24", "02:30", "2026-10-24T00:30Z", 24)]
    [InlineData("2026-10-25", "02:30", "2026-10-25T00:30Z", 25)] // 02:30 happens twice: the first one
    public void Day_bounds_follow_the_local_clock(string day, string dayStart, string expectedStart, double expectedHours)
    {
        var (start, end) = DaySummaries.Bounds(Day(day), Oslo, Clock(dayStart));

        start.ShouldBe(Utc(expectedStart));
        (end - start).ShouldBe(TimeSpan.FromHours(expectedHours));
    }

    [Fact]
    public void Consecutive_days_tile_without_gaps_or_overlaps()
    {
        foreach (var dayStart in new[] { TimeSpan.Zero, Clock("02:30"), Clock("04:00") })
        {
            for (var day = Day("2026-01-01"); day < Day("2027-01-01"); day = day.AddDays(1))
            {
                DaySummaries.Bounds(day, Oslo, dayStart).End.ShouldBe(DaySummaries.Bounds(day.AddDays(1), Oslo, dayStart).Start);
            }
        }
    }

    [Theory]
    [InlineData("2026-11-05T23:30Z", "00:00", "2026-11-06")]
    [InlineData("2026-11-05T22:59Z", "00:00", "2026-11-05")]
    [InlineData("2026-11-06T01:00Z", "04:00", "2026-11-05")] // 02:00 local belongs to the day before
    [InlineData("2026-11-06T03:00Z", "04:00", "2026-11-06")]
    [InlineData("2026-03-29T00:45Z", "02:30", "2026-03-28")] // 01:45 local, before the jump
    [InlineData("2026-03-29T01:00Z", "02:30", "2026-03-29")] // 03:00 local, right after it
    [InlineData("2026-10-25T00:15Z", "02:30", "2026-10-24")] // first 02:15
    [InlineData("2026-10-25T01:15Z", "02:30", "2026-10-25")] // second 02:15, after the day began at the first 02:30
    public void Day_of_an_instant(string at, string dayStart, string expected)
    {
        DaySummaries.DayOf(Utc(at), Oslo, Clock(dayStart)).ShouldBe(Day(expected));
    }

    [Theory]
    [InlineData("-00:01")]
    [InlineData("06:00")]
    [InlineData("23:00")]
    public void Day_start_must_be_early_morning(string dayStart)
    {
        var value = TimeSpan.Parse(dayStart, CultureInfo.InvariantCulture);

        Should.Throw<ArgumentOutOfRangeException>(() => DaySummaries.Bounds(Day("2026-11-05"), Oslo, value));
    }

    // One interval from 22:00 to 02:00 local is split at midnight.
    [Theory]
    [InlineData("2026-11-05T21:00Z", "2026-11-06T01:00Z", "2026-11-05", "00:00", 120, "2026-11-05T21:00Z", "2026-11-05T23:00Z")]
    [InlineData("2026-11-05T21:00Z", "2026-11-06T01:00Z", "2026-11-06", "00:00", 120, "2026-11-05T23:00Z", "2026-11-06T01:00Z")]
    [InlineData("2026-11-05T21:00Z", "2026-11-06T01:00Z", "2026-11-04", "00:00", 0, null, null)]
    [InlineData("2026-11-05T21:00Z", "2026-11-06T01:00Z", "2026-11-07", "00:00", 0, null, null)]
    // With the day starting at 04:00, the whole evening counts towards the 5th.
    [InlineData("2026-11-05T21:00Z", "2026-11-06T01:00Z", "2026-11-05", "04:00", 240, "2026-11-05T21:00Z", "2026-11-06T01:00Z")]
    [InlineData("2026-11-05T21:00Z", "2026-11-06T01:00Z", "2026-11-06", "04:00", 0, null, null)]
    // An interval covering the whole 23 hour day and some of each neighbour.
    [InlineData("2026-03-28T20:00Z", "2026-03-30T05:00Z", "2026-03-28", "00:00", 180, "2026-03-28T20:00Z", "2026-03-28T23:00Z")]
    [InlineData("2026-03-28T20:00Z", "2026-03-30T05:00Z", "2026-03-29", "00:00", 23 * 60, "2026-03-28T23:00Z", "2026-03-29T22:00Z")]
    [InlineData("2026-03-28T20:00Z", "2026-03-30T05:00Z", "2026-03-30", "00:00", 420, "2026-03-29T22:00Z", "2026-03-30T05:00Z")]
    // The same around the 25 hour day.
    [InlineData("2026-10-24T20:00Z", "2026-10-26T05:00Z", "2026-10-24", "00:00", 120, "2026-10-24T20:00Z", "2026-10-24T22:00Z")]
    [InlineData("2026-10-24T20:00Z", "2026-10-26T05:00Z", "2026-10-25", "00:00", 25 * 60, "2026-10-24T22:00Z", "2026-10-25T23:00Z")]
    [InlineData("2026-10-24T20:00Z", "2026-10-26T05:00Z", "2026-10-26", "00:00", 360, "2026-10-25T23:00Z", "2026-10-26T05:00Z")]
    // An hour of work across the hour that is skipped (01:30 to 03:30 local, one real hour).
    [InlineData("2026-03-29T00:30Z", "2026-03-29T01:30Z", "2026-03-29", "00:00", 60, "2026-03-29T00:30Z", "2026-03-29T01:30Z")]
    // Two real hours across the hour that happens twice (02:30 to 02:30 local).
    [InlineData("2026-10-25T00:30Z", "2026-10-25T01:30Z", "2026-10-25", "00:00", 60, "2026-10-25T00:30Z", "2026-10-25T01:30Z")]
    public void An_interval_crossing_a_day_boundary_is_clipped_to_the_day(
        string start,
        string end,
        string day,
        string dayStart,
        int expectedMinutes,
        string? expectedFirst,
        string? expectedLast)
    {
        var summary = DaySummaries.Build([Active(Utc(start), Utc(end))], Day(day), Oslo, Later, Clock(dayStart));

        summary.Active.ShouldBe(TimeSpan.FromMinutes(expectedMinutes));
        summary.FirstActivity.ShouldBe(expectedFirst is null ? null : Utc(expectedFirst));
        summary.LastActivity.ShouldBe(expectedLast is null ? null : Utc(expectedLast));
        summary.Apps.Sum(a => a.Duration.Ticks).ShouldBe(summary.Active.Ticks);
    }

    [Fact]
    public void Splitting_a_long_interval_into_days_loses_no_time()
    {
        // Across both DST changes of 2026.
        var interval = Active(Utc("2026-03-01T10:17Z"), Utc("2026-11-15T07:43Z"));

        foreach (var dayStart in new[] { TimeSpan.Zero, Clock("02:30"), Clock("04:00") })
        {
            var total = TimeSpan.Zero;
            for (var day = Day("2026-02-28"); day <= Day("2026-11-16"); day = day.AddDays(1))
            {
                var summary = DaySummaries.Build([interval], day, Oslo, Later, dayStart);
                summary.Active.ShouldBeLessThanOrEqualTo(summary.End - summary.Start);
                total += summary.Active;
            }

            total.ShouldBe(interval.Duration);
        }
    }

    [Theory]
    [InlineData("2026-11-06T00:30Z", "2026-11-05", 120)] // still running after midnight: the 5th gets until midnight
    [InlineData("2026-11-06T00:30Z", "2026-11-06", 90)]
    [InlineData("2026-11-05T22:00Z", "2026-11-05", 60)]
    [InlineData("2026-11-05T21:10Z", "2026-11-05", 30)] // checkpoint is ahead of now: never less than what's stored
    public void An_open_interval_runs_until_now(string now, string day, int expectedMinutes)
    {
        var open = Active(Utc("2026-11-05T21:00Z"), Utc("2026-11-05T21:30Z"), open: true);

        var summary = DaySummaries.Build([open], Day(day), Oslo, Utc(now));

        summary.Active.ShouldBe(TimeSpan.FromMinutes(expectedMinutes));
    }

    [Fact]
    public void Away_time_is_counted_separately_and_left_out_of_the_percentages()
    {
        var summary = DaySummaries.Build(
            [
                Active(Utc("2026-11-05T07:00Z"), Utc("2026-11-05T09:00Z"), Code),
                Away(Utc("2026-11-05T09:00Z"), Utc("2026-11-05T12:00Z")),
                Active(Utc("2026-11-05T12:00Z"), Utc("2026-11-05T13:00Z"), Outlook),
                Away(Utc("2026-11-05T13:00Z"), Utc("2026-11-05T13:30Z"), open: true),
            ],
            Day("2026-11-05"),
            Oslo,
            Utc("2026-11-05T14:00Z"));

        summary.Active.ShouldBe(TimeSpan.FromHours(3));
        summary.Away.ShouldBe(TimeSpan.FromHours(4)); // the open away interval runs until now
        summary.FirstActivity.ShouldBe(Utc("2026-11-05T07:00Z"));
        summary.LastActivity.ShouldBe(Utc("2026-11-05T13:00Z"));
        summary.Apps.Select(a => (a.App, a.Duration, a.Percent)).ShouldBe(
        [
            (Code, TimeSpan.FromHours(2), 67),
            (Outlook, TimeSpan.FromHours(1), 33),
        ]);
        summary.Apps.Sum(a => a.Share).ShouldBe(1, tolerance: 1e-12);
    }

    [Fact]
    public void An_empty_day_gives_an_empty_valid_summary()
    {
        var summary = DaySummaries.Build([], Day("2026-03-29"), Oslo, Later);

        summary.ShouldBe(summary with
        {
            Day = Day("2026-03-29"),
            Start = Utc("2026-03-28T23:00Z"),
            End = Utc("2026-03-29T22:00Z"),
            Active = TimeSpan.Zero,
            Away = TimeSpan.Zero,
            FirstActivity = null,
            LastActivity = null,
        });
        summary.Apps.ShouldBeEmpty();
    }

    [Fact]
    public void A_day_with_only_away_time_has_no_apps()
    {
        var summary = DaySummaries.Build([Away(Utc("2026-11-05T07:00Z"), Utc("2026-11-05T09:00Z"))], Day("2026-11-05"), Oslo, Later);

        summary.Active.ShouldBe(TimeSpan.Zero);
        summary.Away.ShouldBe(TimeSpan.FromHours(2));
        summary.FirstActivity.ShouldBeNull();
        summary.Apps.ShouldBeEmpty();
    }

    [Fact]
    public void A_day_in_the_future_is_empty_even_with_an_open_interval()
    {
        var open = Active(Utc("2026-11-05T21:00Z"), Utc("2026-11-05T21:30Z"), open: true);

        var summary = DaySummaries.Build([open], Day("2026-11-07"), Oslo, Utc("2026-11-05T22:00Z"));

        summary.Active.ShouldBe(TimeSpan.Zero);
    }

    [Fact]
    public void Apps_are_grouped_by_key_and_ordered_by_time()
    {
        var renamed = Code with { DisplayName = "VS Code" };
        var summary = DaySummaries.Build(
            [
                Active(Utc("2026-11-05T08:00Z"), Utc("2026-11-05T08:30Z"), Code),
                Active(Utc("2026-11-05T08:30Z"), Utc("2026-11-05T09:00Z"), Teams),
                new(Guid.NewGuid(), IntervalKind.Active, Utc("2026-11-05T09:00Z"), Utc("2026-11-05T09:10Z"), false, null, null, null),
                Active(Utc("2026-11-05T09:10Z"), Utc("2026-11-05T09:50Z"), renamed),
                Active(Utc("2026-11-05T09:50Z"), Utc("2026-11-05T10:20Z"), Outlook),
            ],
            Day("2026-11-05"),
            Oslo,
            Later);

        summary.Apps.Select(a => (a.App.DisplayName, a.Duration.TotalMinutes)).ShouldBe(
        [
            ("VS Code", 70), // the latest name
            ("Outlook", 30), // a tie goes by name
            ("Teams", 30),
            ("Unknown", 10),
        ]);
    }

    [Fact]
    public void Percentages_of_equal_apps_add_up_to_100()
    {
        AppIdentity[] apps = [.. Enumerable.Range(0, 7).Select(i => new AppIdentity($"app{i}", $"App {i}"))];
        var start = Utc("2026-11-05T08:00Z");

        var summary = DaySummaries.Build(
            apps.Select((app, i) => Active(start.AddMinutes(10 * i), start.AddMinutes(10 * (i + 1)), app)),
            Day("2026-11-05"),
            Oslo,
            Later);

        summary.Apps.Sum(a => a.Percent).ShouldBe(100);
        summary.Apps.Select(a => a.Percent).ShouldBe([15, 15, 14, 14, 14, 14, 14]);
    }

    [Theory]
    [InlineData(new long[] { 1 }, new[] { 100 })]
    [InlineData(new long[] { 2, 1 }, new[] { 67, 33 })]
    [InlineData(new long[] { 1, 1, 1 }, new[] { 34, 33, 33 })]
    [InlineData(new long[] { 1, 1, 1, 1, 1, 1 }, new[] { 17, 17, 17, 17, 16, 16 })]
    [InlineData(new long[] { 1, 2, 3, 4 }, new[] { 10, 20, 30, 40 })]
    [InlineData(new long[] { 0, 5 }, new[] { 0, 100 })]
    [InlineData(new long[] { 998, 1, 1 }, new[] { 100, 0, 0 })]
    [InlineData(new long[] { 9930, 35, 35 }, new[] { 99, 1, 0 })]
    [InlineData(new long[] { 0, 0 }, new[] { 0, 0 })]
    [InlineData(new long[0], new int[0])]
    public void Rounding_gives_percentages_that_add_up_to_100(long[] parts, int[] expected)
    {
        DaySummaries.RoundToHundred(parts).ShouldBe(expected);
    }

    [Fact]
    public void Rounding_always_sums_to_100_and_stays_within_one_point()
    {
        var random = new Random(25);
        for (var run = 0; run < 2000; run++)
        {
            long[] parts = [.. Enumerable.Range(0, random.Next(1, 30)).Select(_ => random.NextInt64(0, TimeSpan.TicksPerDay))];
            var sum = parts.Sum();

            var percents = DaySummaries.RoundToHundred(parts);

            percents.Sum().ShouldBe(sum == 0 ? 0 : 100);
            for (var i = 0; i < parts.Length; i++)
            {
                var exact = sum == 0 ? 0 : 100.0 * parts[i] / sum;
                Math.Abs(percents[i] - exact).ShouldBeLessThan(1);
            }
        }
    }
}
