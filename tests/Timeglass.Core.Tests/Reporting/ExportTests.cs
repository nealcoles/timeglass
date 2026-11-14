using Timeglass.Core.Domain;
using Timeglass.Core.Reporting;
using static Timeglass.Core.Tests.Reporting.ReportingData;

namespace Timeglass.Core.Tests.Reporting;

public sealed class ExportTests
{
    private static readonly ExportPrivacy NoPrivacy = ExportPrivacy.None;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<List<ExportedInterval>> IntervalsAsync(
        ActivityInterval[] intervals,
        string from,
        string to,
        DateTimeOffset? now = null,
        ExportPrivacy? privacy = null) =>
        await Exports.Intervals(intervals.ToAsyncEnumerable(), Utc(from), Utc(to), now ?? Later, privacy ?? NoPrivacy, Ct).ToListAsync(Ct);

    private static async Task<List<string>> DailyAppsAsync(
        ActivityInterval[] intervals,
        string firstDay,
        string lastDay,
        DateTimeOffset? now = null,
        ExportPrivacy? privacy = null,
        TimeSpan dayStart = default) =>
        await Exports.DailyApps(intervals.ToAsyncEnumerable(), Day(firstDay), Day(lastDay), Oslo, now ?? Later, privacy ?? NoPrivacy, dayStart, Ct)
            .Select(t => $"{t.Day:MM-dd} {t.App.Key} {t.Duration.TotalMinutes}")
            .ToListAsync(Ct);

    [Fact]
    public async Task Intervals_are_clipped_to_the_range_and_keep_their_order()
    {
        ActivityInterval[] stored =
        [
            Active(Utc("2026-11-04T06:00Z"), Utc("2026-11-04T08:00Z"), Code, "main.cs"),
            Away(Utc("2026-11-04T08:00Z"), Utc("2026-11-04T09:00Z")),
            Active(Utc("2026-11-04T09:00Z"), Utc("2026-11-04T12:00Z"), Outlook, "Inbox"),
        ];

        var exported = await IntervalsAsync(stored, "2026-11-04T07:00Z", "2026-11-04T10:00Z");

        exported.ShouldBe(
        [
            new ExportedInterval(IntervalKind.Active, Utc("2026-11-04T07:00Z"), Utc("2026-11-04T08:00Z"), Code, "main.cs", false),
            new ExportedInterval(IntervalKind.Away, Utc("2026-11-04T08:00Z"), Utc("2026-11-04T09:00Z"), null, null, false),
            new ExportedInterval(IntervalKind.Active, Utc("2026-11-04T09:00Z"), Utc("2026-11-04T10:00Z"), Outlook, "Inbox", false),
        ]);
        exported[0].Duration.ShouldBe(TimeSpan.FromHours(1));
    }

    [Fact]
    public async Task Intervals_outside_the_range_are_left_out()
    {
        ActivityInterval[] stored =
        [
            Active(Utc("2026-11-04T06:00Z"), Utc("2026-11-04T07:00Z")), // ends where the range starts
            Active(Utc("2026-11-04T10:00Z"), Utc("2026-11-04T11:00Z")), // starts where it ends
        ];

        (await IntervalsAsync(stored, "2026-11-04T07:00Z", "2026-11-04T10:00Z")).ShouldBeEmpty();
    }

    [Fact]
    public async Task An_open_interval_runs_until_now()
    {
        ActivityInterval[] stored = [Active(Utc("2026-11-04T09:00Z"), Utc("2026-11-04T09:10Z"), Code, "main.cs", open: true)];

        var exported = await IntervalsAsync(stored, "2026-11-04T00:00Z", "2026-11-05T00:00Z", now: Utc("2026-11-04T09:30Z"));

        exported.ShouldHaveSingleItem().ShouldBe(
            new ExportedInterval(IntervalKind.Active, Utc("2026-11-04T09:00Z"), Utc("2026-11-04T09:30Z"), Code, "main.cs", true));
    }

    [Fact]
    public async Task Active_time_without_an_app_is_exported_as_unknown()
    {
        ActivityInterval[] stored = [Active(Utc("2026-11-04T09:00Z"), Utc("2026-11-04T10:00Z")) with { App = null }];

        (await IntervalsAsync(stored, "2026-11-04T00:00Z", "2026-11-05T00:00Z")).ShouldHaveSingleItem().App.ShouldBe(AppIdentity.Unknown);
        (await IntervalsAsync(stored, "2026-11-04T00:00Z", "2026-11-05T00:00Z", privacy: new ExportPrivacy(excludedApps: ["unknown"]))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Excluded_apps_are_left_out_and_hidden_titles_are_not_exported()
    {
        ActivityInterval[] stored =
        [
            Active(Utc("2026-11-04T07:00Z"), Utc("2026-11-04T08:00Z"), Code, "main.cs"),
            Active(Utc("2026-11-04T08:00Z"), Utc("2026-11-04T09:00Z"), Outlook, "Inbox"),
            Away(Utc("2026-11-04T09:00Z"), Utc("2026-11-04T10:00Z")),
            Active(Utc("2026-11-04T10:00Z"), Utc("2026-11-04T11:00Z"), Teams, "Standup"),
        ];

        // Keys match as the tracker matches them: trimmed, without case.
        var privacy = new ExportPrivacy(excludedApps: [" Outlook "], appsWithoutTitles: ["TEAMS"]);
        var exported = await IntervalsAsync(stored, "2026-11-04T00:00Z", "2026-11-05T00:00Z", privacy: privacy);

        exported.Select(i => (i.App?.Key, i.Title)).ShouldBe([("code", "main.cs"), (null, null), ("teams", null)]);
    }

    [Fact]
    public async Task No_title_is_exported_when_titles_are_turned_off()
    {
        ActivityInterval[] stored =
        [
            Active(Utc("2026-11-04T07:00Z"), Utc("2026-11-04T08:00Z"), Code, "main.cs"),
            Active(Utc("2026-11-04T08:00Z"), Utc("2026-11-04T09:00Z"), Outlook, "Inbox"),
        ];

        var exported = await IntervalsAsync(stored, "2026-11-04T00:00Z", "2026-11-05T00:00Z", privacy: new ExportPrivacy(includeTitles: false));

        exported.Select(i => (i.App?.Key, i.Title)).ShouldBe([("code", null), ("outlook", null)]);
    }

    [Fact]
    public async Task Daily_apps_are_in_date_order_with_the_most_time_first()
    {
        // Oslo is UTC+1: the late evening runs from 23:00 on the 4th to 01:00 on the 5th.
        ActivityInterval[] stored =
        [
            Active(Utc("2026-11-04T07:00Z"), Utc("2026-11-04T07:30Z"), Outlook),
            Active(Utc("2026-11-04T07:30Z"), Utc("2026-11-04T09:00Z"), Code),
            Away(Utc("2026-11-04T09:00Z"), Utc("2026-11-04T10:00Z")),
            Active(Utc("2026-11-04T10:00Z"), Utc("2026-11-04T10:15Z"), Outlook),
            Active(Utc("2026-11-04T22:00Z"), Utc("2026-11-05T00:00Z"), Code),
            Active(Utc("2026-11-07T08:00Z"), Utc("2026-11-07T08:20Z"), Teams),
            Active(Utc("2026-11-07T08:20Z"), Utc("2026-11-07T08:40Z"), Code),
        ];

        (await DailyAppsAsync(stored, "2026-11-02", "2026-11-08")).ShouldBe(
        [
            "11-04 code 150",
            "11-04 outlook 45",
            "11-05 code 60",
            "11-07 code 20", // a tie: by name
            "11-07 teams 20",
        ]);
    }

    [Fact]
    public async Task Daily_apps_clip_to_the_range_and_count_the_open_interval_until_now()
    {
        ActivityInterval[] stored =
        [
            Active(Utc("2026-11-03T20:00Z"), Utc("2026-11-04T01:00Z"), Code), // from 21:00 the day before
            Active(Utc("2026-11-04T08:00Z"), Utc("2026-11-04T08:10Z"), Outlook, open: true),
        ];

        (await DailyAppsAsync(stored, "2026-11-04", "2026-11-04", now: Utc("2026-11-04T08:30Z")))
            .ShouldBe(["11-04 code 120", "11-04 outlook 30"]);
    }

    [Fact]
    public async Task Daily_apps_split_an_interval_over_every_day_it_covers()
    {
        ActivityInterval[] stored =
        [
            Active(Utc("2026-11-04T22:00Z"), Utc("2026-11-06T23:30Z"), Code), // 23:00 on the 4th to 00:30 on the 7th
            Active(Utc("2026-11-06T23:30Z"), Utc("2026-11-07T00:00Z"), Outlook),
        ];

        (await DailyAppsAsync(stored, "2026-11-01", "2026-11-30")).ShouldBe(
        [
            "11-04 code 60",
            "11-05 code 1440",
            "11-06 code 1440",
            "11-07 code 30",
            "11-07 outlook 30",
        ]);
    }

    [Fact]
    public async Task Daily_apps_follow_the_day_start_and_daylight_saving()
    {
        // 25 October 2026 is 25 hours long in Oslo.
        ActivityInterval[] stored = [Active(Utc("2026-10-24T20:00Z"), Utc("2026-10-26T02:00Z"), Code)];

        (await DailyAppsAsync(stored, "2026-10-24", "2026-10-26")).ShouldBe(["10-24 code 120", "10-25 code 1500", "10-26 code 180"]);
        (await DailyAppsAsync(stored, "2026-10-24", "2026-10-26", dayStart: TimeSpan.FromHours(4)))
            .ShouldBe(["10-24 code 420", "10-25 code 1380"]); // 22:00 to 04:00 with the extra hour, then until 03:00
    }

    [Fact]
    public async Task Daily_apps_leave_out_excluded_apps_and_keep_the_latest_name()
    {
        ActivityInterval[] stored =
        [
            Active(Utc("2026-11-04T07:00Z"), Utc("2026-11-04T08:00Z"), Code),
            Active(Utc("2026-11-04T08:00Z"), Utc("2026-11-04T09:00Z"), Outlook),
            Active(Utc("2026-11-04T09:00Z"), Utc("2026-11-04T10:00Z"), new AppIdentity("code", "Visual Studio Code")),
        ];

        var totals = await Exports.DailyApps(stored.ToAsyncEnumerable(), Day("2026-11-04"), Day("2026-11-04"), Oslo, Later, new ExportPrivacy(excludedApps: ["outlook"]), cancellationToken: Ct)
            .ToListAsync(Ct);

        totals.ShouldHaveSingleItem().ShouldBe(new DailyAppTotal(Day("2026-11-04"), new AppIdentity("code", "Visual Studio Code"), TimeSpan.FromHours(2)));
    }

    [Fact]
    public async Task Daily_apps_agree_with_the_day_summaries()
    {
        var origin = Utc("2026-11-02T06:00Z");
        var stored = Parse(
            origin,
            "code 0-3600 main.cs",
            "outlook 3600-5400 Inbox",
            "away 5400-9000",
            "teams 9000-12600 Standup",
            "code 60000-100000 late.cs", // past midnight
            "outlook 100000-180000 Inbox", // and past the next one
            "code 200000-200300*");
        var now = origin.AddSeconds(201000);

        var totals = await Exports.DailyApps(stored.ToAsyncEnumerable(), Day("2026-11-01"), Day("2026-11-07"), Oslo, now, NoPrivacy, cancellationToken: Ct).ToListAsync(Ct);

        var range = RangeSummaries.Build(stored, Day("2026-11-01"), Day("2026-11-07"), Oslo, now, topApps: int.MaxValue);
        totals.ShouldBe(range.Days.SelectMany(d => d.TopApps.Select(a => new DailyAppTotal(d.Day, a.App, a.Duration))));
        totals.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task Daily_apps_hand_out_a_day_as_soon_as_a_later_one_starts()
    {
        var read = 0;
        async IAsyncEnumerable<ActivityInterval> Stored()
        {
            for (var day = 0; day < 5; day++)
            {
                read++;
                yield return Active(Utc("2026-11-02T08:00Z").AddDays(day), Utc("2026-11-02T09:00Z").AddDays(day), Code);
                await Task.Yield();
            }
        }

        await using var totals = Exports.DailyApps(Stored(), Day("2026-11-02"), Day("2026-11-06"), Oslo, Later, NoPrivacy, cancellationToken: Ct).GetAsyncEnumerator(Ct);

        (await totals.MoveNextAsync()).ShouldBeTrue();
        totals.Current.Day.ShouldBe(Day("2026-11-02"));
        read.ShouldBe(2); // the first day is out once the second day's first interval is seen
    }

    [Fact]
    public void A_range_must_not_end_before_it_starts()
    {
        Should.Throw<ArgumentException>(() => Exports.Bounds(Day("2026-11-05"), Day("2026-11-04"), Oslo));

        // Unlike the range summary, any length is fine.
        var (start, end) = Exports.Bounds(Day("2020-01-01"), Day("2026-11-05"), Oslo);
        start.ShouldBe(Utc("2019-12-31T23:00Z"));
        end.ShouldBe(Utc("2026-11-05T23:00Z"));
    }

    [Fact]
    public void Privacy_rules_can_be_asked_directly()
    {
        var privacy = new ExportPrivacy(includeTitles: true, excludedApps: ["outlook", null!], appsWithoutTitles: ["teams"]);

        privacy.IncludeTitles.ShouldBeTrue();
        privacy.Excludes(Outlook).ShouldBeTrue();
        privacy.Excludes(Code).ShouldBeFalse();
        privacy.TitleFor(Code, "main.cs").ShouldBe("main.cs");
        privacy.TitleFor(Teams, "Standup").ShouldBeNull();
        ExportPrivacy.None.Excludes(Outlook).ShouldBeFalse();
        Should.Throw<ArgumentNullException>(() => privacy.Excludes(null!));
        Should.Throw<ArgumentNullException>(() => privacy.TitleFor(null!, "x"));
    }
}
