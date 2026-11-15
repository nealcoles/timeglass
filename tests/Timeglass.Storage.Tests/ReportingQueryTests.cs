using System.Globalization;
using Dapper;
using Timeglass.Core.Domain;
using Timeglass.Core.Reporting;

namespace Timeglass.Storage.Tests;

/// <summary>The reporting queries against a real, seeded database.</summary>
public sealed class ReportingQueryTests : IAsyncLifetime
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
    private static readonly AppIdentity Code = new("code", "Code");
    private static readonly AppIdentity Outlook = new("outlook", "Outlook");
    private static readonly AppIdentity Teams = new("teams", "Teams");

    // Wednesday 4 November 2026 and Thursday the 5th, Oslo (UTC+1), with a late evening in between.
    // Now is 10:00 local on Thursday, with Code still open since 09:30.
    private static readonly ActivityInterval[] Seed =
    [
        Active("2026-11-04T07:00Z", "2026-11-04T08:00Z", Code, "main.cs"),
        Active("2026-11-04T08:00:03Z", "2026-11-04T08:30Z", Code, "tests.cs"), // 3 s after: one timeline block
        Active("2026-11-04T08:30Z", "2026-11-04T09:00Z", Outlook, "Inbox"),
        Away("2026-11-04T09:00Z", "2026-11-04T10:00Z"),
        Active("2026-11-04T10:00Z", "2026-11-04T10:20Z", Code, "main.cs"),
        Active("2026-11-04T10:20Z", "2026-11-04T10:30Z", Code, null),
        Active("2026-11-04T22:00Z", "2026-11-05T00:00Z", Code, "late.cs"), // 23:00 to 01:00 local
        Active("2026-11-05T07:00Z", "2026-11-05T08:00Z", Outlook, "Inbox"),
        Active("2026-11-05T08:00Z", "2026-11-05T08:30Z", Outlook, "RE: Budget"),
        Active("2026-11-05T08:30Z", "2026-11-05T08:40Z", Code, "main.cs", open: true),
    ];

    private static readonly DateTimeOffset Now = Utc("2026-11-05T09:00Z");

    private readonly TempDatabase _temp = new();
    private TimeglassDatabase _database = null!;
    private IntervalRepository _repository = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = _temp.Open();
        _repository = new IntervalRepository(_database);
        await _repository.ApplyAsync(
            [.. Seed.Select(i => new IntervalChange(i.IsOpen ? IntervalChangeKind.Opened : IntervalChangeKind.Closed, i))], Ct);
        await _repository.FlushAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _repository.DisposeAsync();
        _database.Dispose();
        _temp.Dispose();
    }

    private static DateTimeOffset Utc(string at) => DateTimeOffset.Parse(at, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal);

    private static DateOnly Day(string day) => DateOnly.ParseExact(day, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static ActivityInterval Active(string start, string end, AppIdentity app, string? title, bool open = false) =>
        new(Guid.CreateVersion7(), IntervalKind.Active, Utc(start), Utc(end), open, app, $@"C:\{app.Key}.exe", title);

    private static ActivityInterval Away(string start, string end) =>
        new(Guid.CreateVersion7(), IntervalKind.Away, Utc(start), Utc(end), false, null, null, null);

    private static string Local(DateTimeOffset at) => TimeZoneInfo.ConvertTime(at, Oslo).ToString("HH:mm", CultureInfo.InvariantCulture);

    [Theory]
    [InlineData("2026-11-04", "00:00", "code 08:00-09:30, outlook 09:30-10:00, away 10:00-11:00, code 11:00-11:30, code 23:00-00:00")]
    [InlineData("2026-11-05", "00:00", "code 00:00-01:00, outlook 08:00-09:30, code 09:30-10:00*")]
    [InlineData("2026-11-05", "04:00", "outlook 08:00-09:30, code 09:30-10:00*")] // the late evening belongs to the 4th
    [InlineData("2026-11-03", "00:00", "")]
    public async Task Timeline_of_a_day(string day, string dayStart, string expected)
    {
        var timeline = await _repository.GetTimelineAsync(Day(day), Oslo, Now, TimeSpan.Parse(dayStart, CultureInfo.InvariantCulture), cancellationToken: Ct);

        string.Join(", ", timeline.Blocks.Select(b => $"{b.App?.Key ?? "away"} {Local(b.Start)}-{Local(b.End)}{(b.IsOpen ? "*" : "")}"))
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("code", "2026-11-04", "2026-11-04", 10, 180, "main.cs 80, late.cs 60, tests.cs 30, (none) 10")]
    [InlineData("code", "2026-11-04", "2026-11-04", 2, 180, "main.cs 80, late.cs 60, 2 other 40")]
    [InlineData("code", "2026-11-04", "2026-11-05", 10, 270, "late.cs 120, main.cs 110, tests.cs 30, (none) 10")]
    [InlineData("outlook", "2026-11-04", "2026-11-05", 10, 120, "Inbox 90, RE: Budget 30")]
    [InlineData("outlook", "2026-11-05", "2026-11-05", 1, 90, "Inbox 60, 1 other 30")]
    [InlineData("teams", "2026-11-04", "2026-11-05", 10, 0, "")]
    public async Task Title_drill_down(string app, string firstDay, string lastDay, int top, int expectedMinutes, string expected)
    {
        var breakdown = await _repository.GetTitleBreakdownAsync(app, Day(firstDay), Day(lastDay), Oslo, Now, top: top, cancellationToken: Ct);

        breakdown.App.Key.ShouldBe(app);
        Math.Round(breakdown.Total.TotalMinutes).ShouldBe(expectedMinutes);
        string.Join(", ", breakdown.Titles
                .Select(t => $"{t.Title ?? "(none)"} {Math.Round(t.Duration.TotalMinutes)}")
                .Concat(breakdown.Other is { } other ? [$"{other.Count} other {Math.Round(other.Duration.TotalMinutes)}"] : []))
            .ShouldBe(expected);
    }

    [Theory]
    [InlineData("2026-11-02", "2026-11-08", 2, new[] { "0", "0", "210 code:180 outlook:30", "180 code:90 outlook:90", "0", "0", "0" })]
    [InlineData("2026-11-04", "2026-11-05", 1, new[] { "210 code:180", "180 code:90" })]
    [InlineData("2026-11-05", "2026-11-05", 3, new[] { "180 code:90 outlook:90" })]
    public async Task Week_overview(string firstDay, string lastDay, int topApps, string[] expected)
    {
        var summary = await _repository.GetRangeSummaryAsync(Day(firstDay), Day(lastDay), Oslo, Now, topApps: topApps, cancellationToken: Ct);

        summary.Days.Select(d => $"{Math.Round(d.Active.TotalMinutes)}{string.Concat(d.TopApps.Select(a => $" {a.App.Key}:{Math.Round(a.Duration.TotalMinutes)}"))}")
            .ShouldBe(expected);
        summary.Active.ShouldBe(new TimeSpan(summary.Days.Sum(d => d.Active.Ticks)));
    }

    [Fact]
    public async Task Week_overview_totals_per_app()
    {
        var summary = await _repository.GetRangeSummaryAsync(Day("2026-11-02"), Day("2026-11-08"), Oslo, Now, cancellationToken: Ct);

        summary.Active.ShouldBe(TimeSpan.FromMinutes(390) - TimeSpan.FromSeconds(3));
        summary.Away.ShouldBe(TimeSpan.FromMinutes(60));
        summary.Apps.Select(a => (a.App.Key, Math.Round(a.Duration.TotalMinutes), a.Percent)).ShouldBe([("code", 270, 69), ("outlook", 120, 31)]);
    }

    [Fact]
    public async Task Range_query_finds_an_interval_that_started_long_before_the_range()
    {
        // A long away stretch over a weekend, then nothing. A day in the middle of it still sees it.
        var weekend = Away("2026-11-06T16:00Z", "2026-11-09T07:00Z");
        await _repository.ApplyAsync([new IntervalChange(IntervalChangeKind.Closed, weekend)], Ct);
        await _repository.FlushAsync(Ct);

        var intervals = await _repository.GetRangeAsync(Utc("2026-11-07T23:00Z"), Utc("2026-11-08T23:00Z"), Ct);

        intervals.ShouldHaveSingleItem().Id.ShouldBe(weekend.Id);
    }

    [Theory]
    [InlineData("2026-11-03T23:00Z", "2026-11-04T23:00Z")] // one day, cutting the late evening
    [InlineData("2026-11-04T23:00Z", "2026-11-05T23:00Z")] // starts inside an interval, ends after the open one
    [InlineData("2020-01-01T00:00Z", "2030-01-01T00:00Z")]
    [InlineData("2026-10-01T00:00Z", "2026-10-31T00:00Z")] // nothing
    public async Task Streaming_a_range_gives_the_same_intervals_as_reading_it(string from, string to)
    {
        var read = await _repository.GetRangeAsync(Utc(from), Utc(to), Ct);

        var streamed = await _repository.StreamRangeAsync(Utc(from), Utc(to), Ct).ToListAsync(Ct);

        streamed.ShouldBe(read);
    }

    [Fact]
    public async Task A_streamed_range_is_read_as_it_is_asked_for_and_can_be_abandoned()
    {
        await using (var intervals = _repository.StreamRangeAsync(Utc("2020-01-01T00:00Z"), Utc("2030-01-01T00:00Z"), Ct).GetAsyncEnumerator(Ct))
        {
            (await intervals.MoveNextAsync()).ShouldBeTrue();
            intervals.Current.Title.ShouldBe("main.cs");
        }

        // The abandoned query holds nothing: writes and a new query go through.
        await _repository.DeleteRangeAsync(Utc("2026-11-04T00:00Z"), Utc("2026-11-05T00:00Z"), Ct);
        var left = await _repository.StreamRangeAsync(Utc("2020-01-01T00:00Z"), Utc("2030-01-01T00:00Z"), Ct).ToListAsync(Ct);
        left.Select(i => i.Title).ShouldBe(["Inbox", "RE: Budget", "main.cs"]);
    }

    [Fact]
    public async Task A_streamed_range_stops_when_cancelled()
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var seen = 0;

        await Should.ThrowAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in _repository.StreamRangeAsync(Utc("2020-01-01T00:00Z"), Utc("2030-01-01T00:00Z"), cancel.Token))
            {
                seen++;
                await cancel.CancelAsync();
            }
        });

        seen.ShouldBe(1);
    }

    [Fact]
    public async Task Drill_down_finds_active_time_stored_without_an_app_under_unknown()
    {
        var connection = _database.Connect();
        await using (connection)
        {
            await connection.ExecuteAsync(
                "INSERT INTO intervals (id, start_utc, end_utc, kind, app_id, window_title, is_open) VALUES (@Id, @Start, @End, 0, NULL, 'Desktop', 0);",
                new
                {
                    Id = Guid.CreateVersion7().ToString("D"),
                    Start = Utc("2026-11-03T08:00Z").ToUnixTimeMilliseconds(),
                    End = Utc("2026-11-03T08:10Z").ToUnixTimeMilliseconds(),
                });
        }

        var breakdown = await _repository.GetTitleBreakdownAsync(AppIdentity.Unknown.Key, Day("2026-11-03"), Day("2026-11-03"), Oslo, Now, cancellationToken: Ct);

        breakdown.Titles.ShouldHaveSingleItem().ShouldBe(new TitleTotal("Desktop", TimeSpan.FromMinutes(10), 1, 100));
    }
}
