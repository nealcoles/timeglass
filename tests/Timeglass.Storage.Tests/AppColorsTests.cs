using Dapper;
using Microsoft.Data.Sqlite;
using Timeglass.Storage.Migrations;

namespace Timeglass.Storage.Tests;

public sealed class AppColorsTests : IDisposable
{
    private readonly TempDatabase _temp = new();
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    [Fact]
    public async Task Assignments_are_distinct_and_survive_new_apps_and_reopening()
    {
        IReadOnlyDictionary<string, string> first;
        using (var database = _temp.Open())
        {
            using (var connection = database.Connect())
            {
                for (var i = 0; i < 20; i++)
                {
                    connection.Execute("INSERT INTO apps (key, display_name) VALUES (@Key, @Key);", new { Key = $"app{i}" });
                }
            }

            await using var repository = new IntervalRepository(database);
            first = await repository.GetAppColorsAsync(["app0"], Ct);
            first.Count.ShouldBe(20);
            first.Values.Distinct().Count().ShouldBe(20);
            var expanded = await repository.GetAppColorsAsync(["unknown", "app0"], Ct);
            expanded["app0"].ShouldBe(first["app0"]);
            first.Values.ShouldNotContain(expanded["unknown"]);
        }

        using var reopened = _temp.Open();
        await using var again = new IntervalRepository(reopened);
        var saved = await again.GetAppColorsAsync(["app0"], Ct);
        foreach (var (key, color) in first)
        {
            saved[key].ShouldBe(color);
        }
        saved.Count.ShouldBe(21);
    }

    [Fact]
    public async Task Concurrent_requests_keep_the_same_assignments()
    {
        using var database = _temp.Open();
        await using var repository = new IntervalRepository(database);
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(i =>
            Task.Run(() => repository.GetAppColorsAsync([$"app{i}"], Ct), Ct)));

        var all = await repository.GetAppColorsAsync([], Ct);
        all.Count.ShouldBe(5);
        all.Values.Distinct().Count().ShouldBe(5);
        foreach (var result in results)
        {
            foreach (var (key, color) in result)
            {
                all[key].ShouldBe(color);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Older_databases_keep_history_and_get_persistent_colors(int version)
    {
        var day = new DateOnly(2026, 11, 4);
        var start = new DateTimeOffset(2026, 11, 4, 9, 0, 0, TimeSpan.Zero).ToUnixTimeMilliseconds();
        using (var old = _temp.Open(Migrator.Embedded.Take(version).ToList()))
        using (var connection = old.Connect())
        {
            connection.Execute("""
                INSERT INTO apps (id, key, display_name) VALUES (1, 'code', 'Code'), (2, 'mail', 'Mail');
                INSERT INTO intervals (id, start_utc, end_utc, kind, app_id, window_title, is_open)
                VALUES ('0190f2a0-0000-7000-8000-000000000001', @Start, @End, 0, 1, 'main.cs', 0),
                       ('0190f2a0-0000-7000-8000-000000000002', @End, @Next, 0, NULL, NULL, 0);
                """, new { Start = start, End = start + 60_000, Next = start + 120_000 });
            if (version >= 3)
            {
                connection.Execute("""
                    INSERT INTO daily_app_totals (day, kind, app_id, duration_ms, first_utc, last_utc)
                    VALUES ('2026-11-04', 0, 2, 120000, @Start, @End);
                    """, new { Start = start - 120_000, End = start });
            }
        }

        string codeColor;
        using (var database = _temp.Open())
        {
            var backup = database.BackupPath.ShouldNotBeNull();
            backup.ShouldBe(_temp.Path + $".v{version}.bak", StringCompareShould.IgnoreCase);
            using (var copy = new SqliteConnection($"Data Source={backup};Pooling=False"))
            {
                copy.Open();
                Migrator.GetVersion(copy).ShouldBe(version);
                copy.ExecuteScalar<int>("SELECT COUNT(*) FROM intervals;").ShouldBe(2);
                if (version >= 3)
                {
                    copy.ExecuteScalar<int>("SELECT COUNT(*) FROM daily_app_totals;").ShouldBe(1);
                }
            }

            await using var repository = new IntervalRepository(database);
            var summary = await repository.GetDaySummaryAsync(day, TimeZoneInfo.Utc, DateTimeOffset.UtcNow, cancellationToken: Ct);
            summary.Active.ShouldBe(TimeSpan.FromMinutes(version >= 3 ? 4 : 2));
            summary.Apps.Select(a => a.App.Key).ShouldContain("unknown");
            var colors = await repository.GetAppColorsAsync([.. summary.Apps.Select(a => a.App.Key)], Ct);
            colors.Keys.ShouldBe(["code", "mail", "unknown"], ignoreOrder: true);
            colors.Values.Distinct().Count().ShouldBe(3);
            codeColor = colors["code"];
        }

        using var reopened = _temp.Open();
        reopened.BackupPath.ShouldBeNull();
        await using var again = new IntervalRepository(reopened);
        var saved = await again.GetAppColorsAsync(["code"], Ct);
        saved["code"].ShouldBe(codeColor);
    }
}
