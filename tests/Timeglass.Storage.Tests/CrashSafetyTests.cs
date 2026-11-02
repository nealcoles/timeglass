using Dapper;
using Microsoft.Data.Sqlite;
using Timeglass.Core.Apps;
using Timeglass.Core.Domain;
using Timeglass.Core.Tracking;
using Timeglass.Storage.Migrations;
using static Timeglass.Testing.TestWindows;

namespace Timeglass.Storage.Tests;

public sealed class CrashSafetyTests : IDisposable
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 2, 8, 0, 0, TimeSpan.Zero);

    private readonly TempDatabase _temp = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _temp.Dispose();

    private static DateTimeOffset At(double seconds) => T0.AddSeconds(seconds);

    /// <summary>Tracks Outlook for <paramref name="seconds"/>, ticking every second, into a fresh repository.</summary>
    private async Task TrackAsync(double seconds, TimeSpan checkpointInterval, bool stopGracefully)
    {
        using var database = _temp.Open();
        var repository = new IntervalRepository(database);
        var engine = new TrackingEngine(
            new AliasAppIdentityResolver(AppAliasTable.BuiltIn),
            TrackingOptions.Default with { CheckpointInterval = checkpointInterval });

        await repository.ApplyAsync(engine.Handle(new TrackingStarted(At(0), Outlook())), Ct);
        for (var s = 1; s <= seconds; s++)
        {
            await repository.ApplyAsync(engine.Handle(new Tick(At(s), At(s))), Ct);
        }

        if (stopGracefully)
        {
            await repository.ApplyAsync(engine.Handle(new TrackingStopped(At(seconds + 0.5))), Ct);
        }

        // A crash: the process dies after the last write, without stopping the engine.
        await repository.DisposeAsync();
    }

    private async Task<IReadOnlyList<ActivityInterval>> ReopenAsync(Action<TimeglassDatabase>? check = null)
    {
        using var database = _temp.Open();
        check?.Invoke(database);
        await using var repository = new IntervalRepository(database);
        return await repository.GetRangeAsync(At(-60), At(3600), Ct);
    }

    [Theory]
    [InlineData(15, 40, 30)]
    [InlineData(5, 33, 30)]
    [InlineData(1, 12, 12)]
    public async Task A_crash_loses_at_most_one_checkpoint_interval(int checkpointSeconds, int crashAfter, int expectedEnd)
    {
        await TrackAsync(crashAfter, TimeSpan.FromSeconds(checkpointSeconds), stopGracefully: false);

        var intervals = await ReopenAsync(db => db.RecoveredIntervals.ShouldBe(1));

        var interval = intervals.ShouldHaveSingleItem();
        interval.IsOpen.ShouldBeFalse();
        interval.Start.ShouldBe(At(0));
        interval.End.ShouldBe(At(expectedEnd));
    }

    [Fact]
    public async Task The_open_interval_is_stored_as_soon_as_it_starts()
    {
        await TrackAsync(0, TimeSpan.FromSeconds(15), stopGracefully: false);

        var interval = (await ReopenAsync()).ShouldHaveSingleItem();
        interval.Start.ShouldBe(At(0));
        interval.End.ShouldBe(At(0));
        interval.App!.Key.ShouldBe("outlook");
    }

    [Fact]
    public async Task A_graceful_stop_closes_the_interval_exactly()
    {
        await TrackAsync(40, TimeSpan.FromSeconds(15), stopGracefully: true);

        var intervals = await ReopenAsync(db => db.RecoveredIntervals.ShouldBe(0));

        var interval = intervals.ShouldHaveSingleItem();
        interval.IsOpen.ShouldBeFalse();
        interval.End.ShouldBe(At(40.5));
    }

    [Fact]
    public void The_database_allows_only_one_open_interval()
    {
        using var database = _temp.Open();
        using var connection = database.Connect();
        connection.Execute("""
            INSERT INTO intervals (id, start_utc, end_utc, kind, is_open) VALUES ('a', 0, 10, 1, 1);
            INSERT INTO intervals (id, start_utc, end_utc, kind, is_open) VALUES ('b', 10, 20, 1, 0);
            """);

        Should.Throw<SqliteException>(() =>
            connection.Execute("INSERT INTO intervals (id, start_utc, end_utc, kind, is_open) VALUES ('c', 20, 30, 1, 1);"))
            .SqliteErrorCode.ShouldBe(19); // SQLITE_CONSTRAINT
    }

    [Fact]
    public async Task Opening_a_new_interval_closes_one_whose_close_was_lost()
    {
        using var database = _temp.Open();
        await using var repository = new IntervalRepository(database);

        var first = new ActivityInterval(Guid.CreateVersion7(), IntervalKind.Active, At(0), At(15), true, new("outlook", "Outlook"), null, null);
        var second = new ActivityInterval(Guid.CreateVersion7(), IntervalKind.Away, At(20), At(20), true, null, null, null);

        await repository.ApplyAsync([new(IntervalChangeKind.Updated, first)], Ct);
        await repository.FlushAsync(Ct);

        // The write with "first closed at 20" never made it; the next one opens "second".
        await repository.ApplyAsync([new(IntervalChangeKind.Opened, second)], Ct);
        await repository.FlushAsync(Ct);

        var stored = await repository.GetRangeAsync(At(0), At(60), Ct);
        stored.Select(i => (i.Id, i.IsOpen, i.End)).ShouldBe([(first.Id, false, At(15)), (second.Id, true, At(20))]);
    }

    [Fact]
    public void Migrating_closes_intervals_left_open_by_older_versions()
    {
        using (var v1 = _temp.Open(Migrator.Embedded.Take(1).ToList()))
        {
            using var connection = v1.Connect();
            connection.Execute("""
                INSERT INTO intervals (id, start_utc, end_utc, kind, is_open) VALUES ('a', 0, 10, 1, 1);
                INSERT INTO intervals (id, start_utc, end_utc, kind, is_open) VALUES ('b', 10, 20, 1, 1);
                """);
        }

        using var database = _temp.Open();
        using var check = database.Connect();

        check.Query<(string, long, long)>("SELECT id, end_utc, is_open FROM intervals ORDER BY id;")
            .ShouldBe([("a", 10L, 0L), ("b", 20L, 0L)]);
    }
}
