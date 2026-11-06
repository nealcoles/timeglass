using Timeglass.Core.Domain;
using Timeglass.Core.Reporting;
using static Timeglass.Core.Tests.Reporting.ReportingData;

namespace Timeglass.Core.Tests.Reporting;

public class TimelineTests
{
    private static readonly DateTimeOffset T0 = Utc("2026-11-05T08:00Z");

    private static string Describe(IEnumerable<TimelineBlock> blocks) => string.Join(", ", blocks.Select(b =>
        $"{b.App?.Key ?? "away"} {(b.Start - T0).TotalSeconds}-{(b.End - T0).TotalSeconds}{(b.IsOpen ? "*" : "")}"
        + (b.Duration != b.End - b.Start ? $" ({b.Duration.TotalSeconds})" : "")));

    // Each case: the stored intervals, the range and now (seconds after T0), and the blocks expected.
    [Theory]
    [InlineData(new[] { "code 0-60", "code 60-120" }, 0, 1000, 1000, "code 0-120")]
    [InlineData(new[] { "code 0-60", "code 64-120" }, 0, 1000, 1000, "code 0-120 (116)")]
    [InlineData(new[] { "code 0-60", "code 65-120" }, 0, 1000, 1000, "code 0-60, code 65-120")] // 5 s is not "a few seconds" any more
    [InlineData(new[] { "code 0-60", "outlook 60-90", "code 90-120" }, 0, 1000, 1000, "code 0-60, outlook 60-90, code 90-120")]
    [InlineData(new[] { "code 0-60", "outlook 62-90" }, 0, 1000, 1000, "code 0-60, outlook 62-90")] // short gap, different app
    [InlineData(new[] { "code 0-60", "away 60-300", "away 300-400", "code 400-500" }, 0, 1000, 1000, "code 0-60, away 60-400, code 400-500")]
    [InlineData(new[] { "code 0-60", "away 61-300" }, 0, 1000, 1000, "code 0-60, away 61-300")]
    // Clipped to the range, also when merging.
    [InlineData(new[] { "code -100-60", "code 62-200", "outlook 200-300" }, 0, 250, 1000, "code 0-200 (198), outlook 200-250")]
    [InlineData(new[] { "code -100-0", "outlook 250-300" }, 0, 250, 1000, "")]
    // The open interval runs until now, never less than its checkpoint, and keeps the block open.
    [InlineData(new[] { "code 0-60", "code 60-90*" }, 0, 1000, 500, "code 0-500*")]
    [InlineData(new[] { "code 0-60", "code 60-90*" }, 0, 1000, 70, "code 0-90*")]
    [InlineData(new[] { "code 0-60", "code 60-90*" }, 0, 400, 500, "code 0-400*")]
    // Input order doesn't matter.
    [InlineData(new[] { "code 64-120", "outlook 120-130", "code 0-60" }, 0, 1000, 1000, "code 0-120 (116), outlook 120-130")]
    public void Builds_blocks_merging_short_gaps(string[] intervals, int from, int to, int now, string expected)
    {
        var blocks = Timelines.Build(Parse(T0, intervals), T0.AddSeconds(from), T0.AddSeconds(to), T0.AddSeconds(now));

        Describe(blocks).ShouldBe(expected);
    }

    [Theory]
    [InlineData(0, "code 0-120, code 121-180")] // touching intervals always merge
    [InlineData(1, "code 0-120, code 121-180")] // a 1 s gap is not shorter than 1 s
    [InlineData(60, "code 0-180 (179)")]
    public void The_merge_gap_can_be_chosen(int gapSeconds, string expected)
    {
        var intervals = Parse(T0, "code 0-60", "code 60-120", "code 121-180");

        var blocks = Timelines.Build(intervals, T0, T0.AddHours(1), Later, TimeSpan.FromSeconds(gapSeconds));

        Describe(blocks).ShouldBe(expected);
    }

    [Fact]
    public void A_negative_merge_gap_is_rejected()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => Timelines.Build([], T0, T0.AddHours(1), Later, TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void A_merged_block_shows_the_latest_app_name_and_counts_unknown_apps_as_one()
    {
        var renamed = Code with { DisplayName = "VS Code" };
        ActivityInterval[] intervals =
        [
            Active(T0, T0.AddSeconds(60), Code),
            Active(T0.AddSeconds(60), T0.AddSeconds(120), renamed),
            new(Guid.NewGuid(), IntervalKind.Active, T0.AddSeconds(120), T0.AddSeconds(130), false, null, null, null),
            Active(T0.AddSeconds(130), T0.AddSeconds(140), AppIdentity.Unknown),
        ];

        var blocks = Timelines.Build(intervals, T0, T0.AddHours(1), Later);

        blocks.Select(b => (b.App!.DisplayName, b.Duration.TotalSeconds)).ShouldBe([("VS Code", 120), ("Unknown", 20)]);
    }

    [Fact]
    public void A_day_timeline_is_clipped_to_the_local_day()
    {
        // 22:00 to 02:00 local on the night of the 5th, then work in the morning.
        ActivityInterval[] intervals =
        [
            Active(Utc("2026-11-05T21:00Z"), Utc("2026-11-06T01:00Z"), Code),
            Active(Utc("2026-11-06T07:00Z"), Utc("2026-11-06T08:00Z"), Outlook),
        ];

        var timeline = Timelines.ForDay(intervals, Day("2026-11-06"), Oslo, Later);

        timeline.Start.ShouldBe(Utc("2026-11-05T23:00Z"));
        timeline.End.ShouldBe(Utc("2026-11-06T23:00Z"));
        timeline.Blocks.Select(b => (b.App!.Key, b.Start, b.End)).ShouldBe(
        [
            ("code", Utc("2026-11-05T23:00Z"), Utc("2026-11-06T01:00Z")),
            ("outlook", Utc("2026-11-06T07:00Z"), Utc("2026-11-06T08:00Z")),
        ]);
    }

    [Fact]
    public void Blocks_add_up_to_the_day_summary()
    {
        var random = new Random(26);
        var intervals = new List<ActivityInterval>();
        var at = Utc("2026-11-04T20:00Z");
        AppIdentity?[] apps = [Code, Outlook, Teams, null];
        while (at < Utc("2026-11-06T04:00Z"))
        {
            var start = at.AddSeconds(random.Next(0, 4) == 0 ? random.Next(0, 10) : 0);
            var end = start.AddSeconds(random.Next(1, 600));
            var app = apps[random.Next(apps.Length)];
            intervals.Add(app is null ? Away(start, end) : Active(start, end, app));
            at = end;
        }

        var timeline = Timelines.ForDay(intervals, Day("2026-11-05"), Oslo, Later);
        var summary = DaySummaries.Build(intervals, Day("2026-11-05"), Oslo, Later);

        new TimeSpan(timeline.Blocks.Where(b => b.Kind == IntervalKind.Active).Sum(b => b.Duration.Ticks)).ShouldBe(summary.Active);
        new TimeSpan(timeline.Blocks.Where(b => b.Kind == IntervalKind.Away).Sum(b => b.Duration.Ticks)).ShouldBe(summary.Away);
        timeline.Blocks.Zip(timeline.Blocks.Skip(1)).ShouldAllBe(p => p.First.End <= p.Second.Start);
    }
}
