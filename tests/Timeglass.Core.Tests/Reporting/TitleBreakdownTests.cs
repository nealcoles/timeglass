using Timeglass.Core.Domain;
using Timeglass.Core.Reporting;
using static Timeglass.Core.Tests.Reporting.ReportingData;

namespace Timeglass.Core.Tests.Reporting;

public class TitleBreakdownTests
{
    private static readonly DateTimeOffset T0 = Utc("2026-11-05T08:00Z");

    private static string Describe(TitleBreakdown breakdown) => string.Join(", ", breakdown.Titles
        .Select(t => $"{t.Title ?? "(none)"} {t.Duration.TotalSeconds} {t.Percent}%"));

    // Each case: the stored intervals, the app, and the titles expected
    // with their seconds and percentages. The range is the first 1000 seconds.
    [Theory]
    [InlineData(new[] { "code 0-60 a.cs", "code 60-90 b.cs", "code 90-120 a.cs" }, "code", "a.cs 90 75%, b.cs 30 25%")]
    [InlineData(new[] { "code 0-60 a.cs", "outlook 60-200 Inbox", "away 200-300" }, "code", "a.cs 60 100%")]
    [InlineData(new[] { "code 0-60 a.cs", "outlook 60-200 Inbox" }, "outlook", "Inbox 140 100%")]
    [InlineData(new[] { "code 0-60 a.cs" }, "teams", "")]
    // Time without a title is its own group, after a named title on a tie.
    [InlineData(new[] { "code 0-30", "code 30-60 a.cs", "code 60-80" }, "code", "(none) 50 63%, a.cs 30 37%")]
    [InlineData(new[] { "code 0-30", "code 30-60 a.cs" }, "code", "a.cs 30 50%, (none) 30 50%")]
    // Titles are exact: case and spacing count.
    [InlineData(new[] { "code 0-30 a.cs", "code 30-60 A.cs", "code 60-70 a.cs " }, "code", "A.cs 30 43%, a.cs 30 43%, a.cs  10 14%")]
    // Every title is listed, the short ones too (#100).
    [InlineData(new[] { "code 0-50 a", "code 50-90 b", "code 90-120 c", "code 120-140 d", "code 140-150 e" }, "code", "a 50 33%, b 40 27%, c 30 20%, d 20 13%, e 10 7%")]
    // Clipped to the range, and the open interval runs until now (1000).
    [InlineData(new[] { "code -100-100 a", "code 900-1100 b" }, "code", "a 100 50%, b 100 50%")]
    [InlineData(new[] { "code 100-200* a" }, "code", "a 900 100%")]
    public void Groups_an_apps_time_by_title(string[] intervals, string app, string expected)
    {
        var breakdown = TitleBreakdowns.Build(Parse(T0, intervals), app, T0, T0.AddSeconds(1000), T0.AddSeconds(1000));

        Describe(breakdown).ShouldBe(expected);
        breakdown.Titles.Sum(t => t.Duration.Ticks).ShouldBe(breakdown.Total.Ticks);
        if (breakdown.Total > TimeSpan.Zero)
        {
            breakdown.Titles.Sum(t => t.Percent).ShouldBe(100);
            breakdown.Titles.Sum(t => t.Share).ShouldBe(1, tolerance: 1e-12);
        }
    }

    [Fact]
    public void Lists_every_title_however_many_there_are()
    {
        var intervals = Enumerable.Range(0, 40).Select(i => $"code {i * 10}-{(i * 10) + 10} title{i}").ToArray();

        var breakdown = TitleBreakdowns.Build(Parse(T0, intervals), "code", T0, T0.AddSeconds(1000), T0.AddSeconds(1000));

        breakdown.Titles.Count.ShouldBe(40);
        breakdown.Titles.Sum(t => t.Percent).ShouldBe(100);
    }

    [Fact]
    public void The_app_has_its_latest_name_or_the_key_when_it_had_no_time()
    {
        var renamed = Code with { DisplayName = "VS Code" };
        ActivityInterval[] intervals = [Active(T0.AddSeconds(60), T0.AddSeconds(90), renamed), Active(T0, T0.AddSeconds(60), Code)];

        TitleBreakdowns.Build(intervals, "code", T0, T0.AddHours(1), Later).App.ShouldBe(renamed);
        TitleBreakdowns.Build(intervals, "teams", T0, T0.AddHours(1), Later).App.ShouldBe(new AppIdentity("teams", "teams"));
    }

    [Fact]
    public void Intervals_without_an_app_belong_to_unknown()
    {
        ActivityInterval[] intervals =
        [
            new(Guid.NewGuid(), IntervalKind.Active, T0, T0.AddSeconds(60), false, null, null, "Untitled"),
            Active(T0.AddSeconds(60), T0.AddSeconds(90), AppIdentity.Unknown, "Untitled"),
        ];

        var breakdown = TitleBreakdowns.Build(intervals, AppIdentity.Unknown.Key, T0, T0.AddHours(1), Later);

        breakdown.Total.ShouldBe(TimeSpan.FromSeconds(90));
        breakdown.App.ShouldBe(AppIdentity.Unknown);
    }

    [Fact]
    public void A_range_of_days_runs_from_the_first_days_start_to_the_last_days_end()
    {
        ActivityInterval[] intervals =
        [
            Active(Utc("2026-11-01T22:30Z"), Utc("2026-11-01T23:30Z"), Code, "before"), // half in the 1st, half in the 2nd
            Active(Utc("2026-11-03T12:00Z"), Utc("2026-11-03T13:00Z"), Code, "middle"),
            Active(Utc("2026-11-04T22:30Z"), Utc("2026-11-04T23:30Z"), Code, "after"), // half in the 4th, half in the 5th
        ];

        var breakdown = TitleBreakdowns.ForDays(intervals, "code", Day("2026-11-02"), Day("2026-11-04"), Oslo, Later);

        breakdown.Start.ShouldBe(Utc("2026-11-01T23:00Z"));
        breakdown.End.ShouldBe(Utc("2026-11-04T23:00Z"));
        breakdown.Titles.Select(t => (t.Title, t.Duration.TotalMinutes)).ShouldBe([("middle", 60), ("after", 30), ("before", 30)]);
    }

    [Fact]
    public void Arguments_are_checked()
    {
        Should.Throw<ArgumentException>(() => TitleBreakdowns.Build([], "", T0, T0.AddHours(1), Later));
        Should.Throw<ArgumentException>(() => TitleBreakdowns.ForDays([], "code", Day("2026-11-05"), Day("2026-11-04"), Oslo, Later));
    }
}
