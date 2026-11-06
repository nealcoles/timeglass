using System.Diagnostics;
using Dapper;
using Timeglass.Core.Domain;

namespace Timeglass.Storage.Tests;

/// <summary>A smoke test that the reporting queries stay fast with a year of history.</summary>
public sealed class ReportingPerformanceTests : IDisposable
{
    private static readonly TimeZoneInfo Oslo = TimeZoneInfo.FindSystemTimeZoneById("Europe/Oslo");
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(100);

    private readonly TempDatabase _temp = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task A_day_query_over_a_year_of_data_answers_within_100_ms()
    {
        var first = new DateOnly(2025, 11, 1);
        var last = new DateOnly(2026, 10, 31);
        using var database = _temp.Open();
        var count = Seed(database, first, last);
        count.ShouldBeGreaterThanOrEqualTo(500_000);

        await using var repository = new IntervalRepository(database);
        var now = new DateTimeOffset(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

        // Days at the end, in the middle and at the start of the history.
        foreach (var day in new[] { last, new DateOnly(2026, 5, 6), first })
        {
            var summary = await repository.GetDaySummaryAsync(day, Oslo, now, cancellationToken: Ct);
            summary.Active.ShouldBeGreaterThan(TimeSpan.Zero);

            Fastest(() => repository.GetDaySummaryAsync(day, Oslo, now, cancellationToken: Ct)).ShouldBeLessThan(Budget, $"day summary for {day}");
            Fastest(() => repository.GetTimelineAsync(day, Oslo, now, cancellationToken: Ct)).ShouldBeLessThan(Budget, $"timeline for {day}");
            Fastest(() => repository.GetTitleBreakdownAsync("code", day, day, Oslo, now, cancellationToken: Ct)).ShouldBeLessThan(Budget, $"drill-down for {day}");
        }
    }

    /// <summary>The best of a few runs, so a busy build machine doesn't fail the test on a hiccup.</summary>
    private static TimeSpan Fastest<T>(Func<Task<T>> query)
    {
        var best = TimeSpan.MaxValue;
        for (var run = 0; run < 5; run++)
        {
            var watch = Stopwatch.StartNew();
            query().GetAwaiter().GetResult();
            watch.Stop();
            best = watch.Elapsed < best ? watch.Elapsed : best;
        }

        return best;
    }

    /// <summary>
    /// Writes about 1400 intervals a day, 08:00 to 20:00 UTC, of 30 seconds each across five apps
    /// with changing titles, plus away time overnight. Goes straight to SQL for speed.
    /// </summary>
    private static int Seed(TimeglassDatabase database, DateOnly first, DateOnly last)
    {
        using var connection = database.Connect();
        using var transaction = connection.BeginTransaction();

        string[] apps = ["code", "outlook", "teams", "edge", "excel"];
        for (var i = 0; i < apps.Length; i++)
        {
            connection.Execute("INSERT INTO apps (id, key, display_name) VALUES (@Id, @Key, @Key);", new { Id = i + 1, Key = apps[i] }, transaction);
        }

        using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText = """
            INSERT INTO intervals (id, start_utc, end_utc, kind, app_id, exe_path, window_title, is_open)
            VALUES ($id, $start, $end, $kind, $app, NULL, $title, 0);
            """;
        var id = insert.Parameters.Add("$id", Microsoft.Data.Sqlite.SqliteType.Text);
        var start = insert.Parameters.Add("$start", Microsoft.Data.Sqlite.SqliteType.Integer);
        var end = insert.Parameters.Add("$end", Microsoft.Data.Sqlite.SqliteType.Integer);
        var kind = insert.Parameters.Add("$kind", Microsoft.Data.Sqlite.SqliteType.Integer);
        var app = insert.Parameters.Add("$app", Microsoft.Data.Sqlite.SqliteType.Integer);
        var title = insert.Parameters.Add("$title", Microsoft.Data.Sqlite.SqliteType.Text);

        var random = new Random(26);
        var count = 0;
        void Add(DateTimeOffset from, DateTimeOffset to, IntervalKind k, int? appId, string? text)
        {
            id.Value = Guid.CreateVersion7(from).ToString("D");
            start.Value = from.ToUnixTimeMilliseconds();
            end.Value = to.ToUnixTimeMilliseconds();
            kind.Value = (int)k;
            app.Value = (object?)appId ?? DBNull.Value;
            title.Value = (object?)text ?? DBNull.Value;
            insert.ExecuteNonQuery();
            count++;
        }

        for (var day = first; day <= last; day = day.AddDays(1))
        {
            var morning = new DateTimeOffset(day.ToDateTime(new TimeOnly(8, 0)), TimeSpan.Zero);
            var evening = morning.AddHours(12);
            for (var at = morning; at < evening; at = at.AddSeconds(30))
            {
                var appId = random.Next(apps.Length) + 1;
                Add(at, at.AddSeconds(30), IntervalKind.Active, appId, $"Document {random.Next(40)}");
            }

            Add(evening, morning.AddDays(1), IntervalKind.Away, null, null);
        }

        transaction.Commit();
        return count;
    }
}
