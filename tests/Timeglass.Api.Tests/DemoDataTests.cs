using Timeglass.Core.Domain;
using Timeglass.DevHost;
using static Timeglass.Api.Tests.ApiHost;

namespace Timeglass.Api.Tests;

/// <summary>The shape of the demo data that the dev host, these tests and the end-to-end tests rely on.</summary>
public class DemoDataTests
{
    // Thursday 5 November 2026, 14:00 in Oslo.
    private static readonly DateTimeOffset Thursday = Utc("2026-11-05T13:00Z");

    private static DateOnly LocalDay(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, Oslo).DateTime);

    [Fact]
    public void Same_arguments_give_the_same_data()
    {
        DemoData.Generate(Thursday, Oslo).ShouldBe(DemoData.Generate(Thursday, Oslo));
    }

    [Fact]
    public void Another_seed_gives_other_data()
    {
        DemoData.Generate(Thursday, Oslo, seed: 7).ShouldNotBe(DemoData.Generate(Thursday, Oslo));
    }

    [Fact]
    public void A_day_looks_the_same_whichever_day_is_today()
    {
        var tuesday = new DateOnly(2026, 11, 3);
        static IEnumerable<ActivityInterval> On(IEnumerable<ActivityInterval> intervals, DateOnly day) =>
            intervals.Where(i => LocalDay(i.Start) == day);

        var seenOnThursday = On(DemoData.Generate(Thursday, Oslo), tuesday).ToList();
        var seenNextMonday = On(DemoData.Generate(Utc("2026-11-09T15:00Z"), Oslo), tuesday).ToList();

        seenOnThursday.ShouldNotBeEmpty();
        seenNextMonday.ShouldBe(seenOnThursday);
    }

    [Fact]
    public void Intervals_are_ordered_never_overlap_and_stop_at_now()
    {
        var intervals = DemoData.Generate(Thursday, Oslo);

        foreach (var (before, after) in intervals.Zip(intervals.Skip(1)))
        {
            after.Start.ShouldBeGreaterThanOrEqualTo(before.End);
        }

        intervals.ShouldAllBe(i => i.End > i.Start && i.End <= Thursday);
        intervals.Select(i => i.Id).Distinct().Count().ShouldBe(intervals.Count);
    }

    [Fact]
    public void The_interval_running_now_is_the_only_open_one()
    {
        var intervals = DemoData.Generate(Thursday, Oslo);

        intervals.Where(i => i.IsOpen).ShouldHaveSingleItem().ShouldBeSameAs(intervals[^1]);
        intervals[^1].End.ShouldBe(Thursday);
    }

    [Fact]
    public void Covers_the_workdays_before_today_and_skips_the_weekend()
    {
        var days = DemoData.Generate(Thursday, Oslo, workdays: 5).Select(i => LocalDay(i.Start)).Distinct();

        // Thursday back to the Thursday before, without Saturday 31 October and Sunday 1 November.
        days.Select(d => d.Day).ShouldBe([29, 30, 2, 3, 4, 5]);
    }

    [Fact]
    public void Has_every_app_away_time_and_titles()
    {
        var intervals = DemoData.Generate(Thursday, Oslo);

        intervals.Where(i => i.App is not null).Select(i => i.App!.Key).Distinct().Order()
            .ShouldBe(["edge", "outlook", "teams", "terminal", "vscode"]);
        intervals.ShouldContain(i => i.Kind == IntervalKind.Away && i.App == null);
        intervals.Where(i => i.Kind == IntervalKind.Active).ShouldAllBe(i => i.Title != null && i.ExePath != null);
        intervals.ShouldContain(i => i.Title == "Daily standup | Microsoft Teams");
    }

    [Fact]
    public void Has_gaps_of_a_few_seconds_for_the_timeline_to_merge()
    {
        var intervals = DemoData.Generate(Thursday, Oslo);

        intervals.Zip(intervals.Skip(1)).ShouldContain(p => p.Second.Start - p.First.End > TimeSpan.Zero && p.Second.Start - p.First.End < TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void Tuesday_evening_runs_past_midnight()
    {
        var intervals = DemoData.Generate(Thursday, Oslo);

        intervals.ShouldContain(i => LocalDay(i.Start) == new DateOnly(2026, 11, 3) && LocalDay(i.End) == new DateOnly(2026, 11, 4));
    }

    [Theory]
    [InlineData("2026-11-02")]
    [InlineData("2026-11-03")]
    [InlineData("2026-11-04")]
    [InlineData("2026-11-05")]
    [InlineData("2026-11-06")]
    [InlineData("2026-11-07")]
    [InlineData("2026-11-08")]
    public void Whatever_day_today_is_there_is_a_midnight_crossing(string today)
    {
        var intervals = DemoData.Generate(Utc($"{today}T13:00Z"), Oslo);

        intervals.ShouldContain(i => LocalDay(i.Start) != LocalDay(i.End));
    }

    [Fact]
    public void Nothing_today_before_eight()
    {
        var early = Utc("2026-11-05T06:00Z"); // 07:00 in Oslo

        var intervals = DemoData.Generate(early, Oslo);

        intervals.ShouldNotContain(i => LocalDay(i.Start) == new DateOnly(2026, 11, 5));
        intervals.ShouldNotContain(i => i.IsOpen);
    }

    [Fact]
    public void Without_workdays_there_is_only_today()
    {
        DemoData.Generate(Thursday, Oslo, workdays: 0).ShouldAllBe(i => LocalDay(i.Start) == new DateOnly(2026, 11, 5));
        Should.Throw<ArgumentOutOfRangeException>(() => DemoData.Generate(Thursday, Oslo, workdays: -1));
    }
}
