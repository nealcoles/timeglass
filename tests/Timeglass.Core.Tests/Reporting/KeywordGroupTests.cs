using Timeglass.Core.Domain;
using Timeglass.Core.Reporting;
using Timeglass.Core.Settings;
using static Timeglass.Core.Tests.Reporting.ReportingData;

namespace Timeglass.Core.Tests.Reporting;

public class KeywordGroupTests
{
    [Fact]
    public void Application_titles_include_only_time_assigned_to_the_group()
    {
        var first = Day("2026-11-02");
        var last = Day("2026-11-03");
        var start = Utc("2026-11-02T22:30Z");
        KeywordGroupSetting[] groups =
        [
            new() { Name = "First", Keywords = ["first"] },
            new() { Name = "Research", Keywords = ["research"] },
        ];
        ActivityInterval[] intervals =
        [
            Active(start, start.AddHours(1), Code) with { Title = "Research" },
            Active(start.AddHours(1), start.AddHours(2), Code) with { Title = "first research" },
            Active(start.AddHours(2), start.AddHours(3), Code) with { Title = "Other" },
            Active(start.AddHours(3), start.AddHours(4), Outlook) with { Title = "Research" },
        ];

        var details = KeywordGroups.BuildAppTitles(intervals, [], first, last, Oslo, Later, groups, "Research", Code.Key);
        var appTotal = KeywordGroups.BuildForGroup(intervals, [], first, last, Oslo, Later, groups, "Research")
            .Apps.Single(a => a.App.Key == Code.Key);

        details.Total.ShouldBe(appTotal.Duration);
        details.Titles.Select(t => (t.Title, t.Duration.TotalMinutes, t.Percent))
            .ShouldBe([("Research", 60d, 100)]);
    }

    [Fact]
    public void Application_titles_include_retained_and_unrecorded_time_as_untitled()
    {
        var day = Day("2026-11-02");
        var start = Utc("2026-11-02T08:00Z");
        KeywordGroupSetting[] groups = [new() { Name = "Mail", Keywords = ["outlook"] }];
        ActivityInterval[] intervals =
        [
            Active(start, start.AddMinutes(10), Outlook) with { Title = "Inbox" },
            Active(start.AddMinutes(10), start.AddMinutes(15), Outlook),
        ];
        RolledUpTotal[] retained =
        [
            new(day, IntervalKind.Active, Outlook, TimeSpan.FromMinutes(5), start, start.AddMinutes(5)),
        ];

        var details = KeywordGroups.BuildAppTitles(intervals, retained, day, day, Oslo, Later, groups, "Mail", Outlook.Key);
        details.Total.ShouldBe(TimeSpan.FromMinutes(20));
        details.Titles.Select(t => (t.Title, t.Duration.TotalMinutes, t.Percent))
            .ShouldBe([("Inbox", 10d, 50), ((string?)null, 10d, 50)]);
        var retainedOnly = KeywordGroups.BuildAppTitles([], retained, day, day, Oslo, Later, groups, "Mail", Outlook.Key);
        retainedOnly.Total.ShouldBe(TimeSpan.FromMinutes(5));
        retainedOnly.Titles.Single().Title.ShouldBeNull();
    }

    [Fact]
    public void Group_detail_counts_only_matching_intervals_and_retained_applications()
    {
        var day = Day("2026-11-02");
        var start = Utc("2026-11-02T08:00Z");
        KeywordGroupSetting[] groups =
        [
            new() { Name = "First", Keywords = ["first"] },
            new() { Name = "Research", Keywords = ["research", "outlook"] },
        ];
        ActivityInterval[] intervals =
        [
            Active(start, start.AddMinutes(10), Code) with { Title = "Research" },
            Active(start.AddMinutes(10), start.AddMinutes(30), Code) with { Title = "first research" },
            Active(start.AddMinutes(30), start.AddMinutes(35), Outlook) with { Title = "Unrelated" },
            Active(start.AddMinutes(35), start.AddMinutes(40), Teams),
        ];
        RolledUpTotal[] retained =
        [
            new(day, IntervalKind.Active, Outlook, TimeSpan.FromMinutes(3), start, start.AddMinutes(3)),
            new(day, IntervalKind.Active, Code, TimeSpan.FromMinutes(2), start, start.AddMinutes(2)),
        ];

        var result = KeywordGroups.BuildForGroup(intervals, retained, day, day, Oslo, Later, groups, "research");

        result.Active.ShouldBe(TimeSpan.FromMinutes(18));
        result.Apps.Select(a => (a.App.Key, a.Duration.TotalMinutes, a.Percent)).ShouldBe(
            [("code", 10d, 56), ("outlook", 8d, 44)]);
        var ungrouped = KeywordGroups.BuildForGroup(intervals, retained, day, day, Oslo, Later, groups, "Ungrouped");
        ungrouped.Active.ShouldBe(TimeSpan.FromMinutes(7));
    }

    [Fact]
    public void Groups_titles_and_app_names_by_first_match_and_preserves_unmatched_time()
    {
        var first = Day("2026-11-02");
        var last = Day("2026-11-03");
        var start = Utc("2026-11-02T22:30Z");
        KeywordGroupSetting[] groups =
        [
            new() { Name = "Investigator", Keywords = ["INVESTIGATOR", "research"] },
            new() { Name = "Prizm", Keywords = ["prizm"] },
        ];
        ActivityInterval[] intervals =
        [
            Active(start, start.AddHours(1), Code) with { Title = "Investigator and Prizm" },
            Active(start.AddHours(1), start.AddHours(2), new AppIdentity("prizm", "Prizm")),
            Active(start.AddHours(2), start.AddHours(3), Teams),
            Away(start.AddHours(3), start.AddHours(4)),
        ];

        var result = KeywordGroups.Build(intervals, [], first, last, Oslo, Later, groups);

        result.Apps.Select(a => (a.App.DisplayName, a.Duration.TotalHours)).ShouldBe(
            [("Away", 1d), ("Investigator", 1d), ("Prizm", 1d), ("Ungrouped", 1d)]);
        result.Apps.Sum(a => a.Percent).ShouldBe(100);
        result.Apps.Single(a => a.App.Key == "away").Percent.ShouldBe(25);
        result.Days[0].Apps.Single().Duration.ShouldBe(TimeSpan.FromMinutes(30));
        result.Days[1].Active.ShouldBe(TimeSpan.FromMinutes(150));
        result.Away.ShouldBe(TimeSpan.FromHours(1));
        result.Active.ShouldBe(TimeSpan.FromHours(3));
        result.Days[1].Apps.Single(a => a.App.Key == "away").Duration.ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public void Away_group_includes_retained_time_and_stays_separate_from_a_user_group_named_away()
    {
        var day = Day("2026-11-02");
        var start = Utc("2026-11-02T08:00Z");
        ActivityInterval[] intervals =
        [
            Active(start, start.AddMinutes(10), Code) with { Title = "away" },
            Away(start.AddMinutes(10), start.AddMinutes(20)),
        ];
        RolledUpTotal[] retained =
        [
            new(day, IntervalKind.Away, null, TimeSpan.FromMinutes(5), start, start.AddMinutes(5)),
        ];
        KeywordGroupSetting[] groups = [new() { Name = "Away", Keywords = ["away"] }];

        var result = KeywordGroups.Build(intervals, retained, day, day, Oslo, Later, groups);

        result.Active.ShouldBe(TimeSpan.FromMinutes(10));
        result.Away.ShouldBe(TimeSpan.FromMinutes(15));
        result.Apps.Select(a => (a.App.Key, a.App.DisplayName, a.Duration.TotalMinutes, a.Percent))
            .ShouldBe([("away", "Away (tracked)", 15d, 60), ("group-0", "Away", 10d, 40)]);
        result.Days.Single().Apps.Select(a => (a.App.Key, a.Percent)).ShouldBe([("away", 60), ("group-0", 40)]);
    }

    [Fact]
    public void Away_only_day_has_an_away_group_and_no_active_time()
    {
        var day = Day("2026-11-02");
        var start = Utc("2026-11-02T08:00Z");

        var result = KeywordGroups.Build([Away(start, start.AddMinutes(10))], [], day, day, Oslo, Later, []);

        result.Active.ShouldBe(TimeSpan.Zero);
        result.Away.ShouldBe(TimeSpan.FromMinutes(10));
        result.Apps.Single().App.Key.ShouldBe("away");
        result.Apps.Single().Percent.ShouldBe(100);
        result.Days.Single().Apps.Single().Share.ShouldBe(1d);
    }

    [Fact]
    public void Away_crossing_midnight_is_split_between_days()
    {
        var first = Day("2026-11-02");
        var start = Utc("2026-11-02T22:30Z");
        var result = KeywordGroups.Build([Away(start, start.AddHours(2))], [], first, first.AddDays(1), Oslo, Later, []);

        result.Apps.Single().Duration.ShouldBe(TimeSpan.FromHours(2));
        result.Days.Select(d => d.Apps.Single().Duration.TotalMinutes).ShouldBe([30d, 90d]);
    }

    [Fact]
    public void Retained_totals_match_only_application_names()
    {
        var day = Day("2026-11-02");
        var start = Utc("2026-11-02T08:00Z");
        RolledUpTotal[] retained =
        [
            new(day, IntervalKind.Active, new AppIdentity("prizm", "Prizm"), TimeSpan.FromMinutes(20), start, start.AddMinutes(20)),
            new(day, IntervalKind.Active, Code, TimeSpan.FromMinutes(10), start, start.AddMinutes(10)),
        ];
        KeywordGroupSetting[] groups = [new() { Name = "Prizm work", Keywords = ["prizm"] }];

        var result = KeywordGroups.Build([], retained, day, day, Oslo, Later, groups);

        result.Apps.Select(a => (a.App.DisplayName, a.Duration.TotalMinutes)).ShouldBe(
            [("Prizm work", 20d), ("Ungrouped", 10d)]);
    }
}
