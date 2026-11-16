using Dapper;
using Microsoft.Data.Sqlite;
using Timeglass.Storage.Migrations;

namespace Timeglass.Storage.Tests;

public sealed class MigrationTests : IDisposable
{
    private readonly TempDatabase _temp = new();

    public void Dispose() => _temp.Dispose();

    private static string Schema(TimeglassDatabase database)
    {
        using var connection = database.Connect();
        return string.Join(
            "\n",
            connection.Query<string>("SELECT sql FROM sqlite_master WHERE sql IS NOT NULL ORDER BY name;"));
    }

    private static int Version(TimeglassDatabase database)
    {
        using var connection = database.Connect();
        return Migrator.GetVersion(connection);
    }

    [Fact]
    public void Embedded_scripts_are_numbered_from_one_without_gaps()
    {
        Migrator.Embedded.Select(m => m.Version).ShouldBe(Enumerable.Range(1, Migrator.Embedded.Count));
        Migrator.Embedded[0].Name.ShouldBe("0001_initial.sql");
    }

    [Fact]
    public void Migrations_apply_to_an_empty_database()
    {
        using var database = _temp.Open();

        Version(database).ShouldBe(Migrator.LatestVersion);
        using var connection = database.Connect();
        connection.Query<string>("SELECT name FROM sqlite_master WHERE type IN ('table', 'index') ORDER BY name;")
            .ShouldBe(
            [
                "apps",
                "daily_app_totals",
                "intervals",
                "ix_intervals_start_utc",
                "sqlite_autoindex_apps_1",
                "sqlite_autoindex_intervals_1",
                "ux_daily_app_totals",
                "ux_intervals_single_open",
            ]);
    }

    [Fact]
    public void Migrations_are_idempotent()
    {
        string first;
        using (var database = _temp.Open())
        {
            first = Schema(database);
        }

        using var again = _temp.Open();
        using var connection = again.Connect();

        Migrator.Apply(connection, Migrator.Embedded).ShouldBe(0);
        Schema(again).ShouldBe(first);
        Version(again).ShouldBe(Migrator.LatestVersion);
    }

    [Fact]
    public void Migrating_from_v1_to_a_later_version_keeps_data()
    {
        using (var v1 = _temp.Open(Migrator.Embedded.Take(1).ToList()))
        {
            using var connection = v1.Connect();
            connection.Execute("""
                INSERT INTO apps (id, key, display_name) VALUES (1, 'outlook', 'Outlook');
                INSERT INTO intervals (id, start_utc, end_utc, kind, app_id, exe_path, window_title, is_open)
                VALUES ('0190f2a0-0000-7000-8000-000000000001', 1000, 61000, 0, 1, 'C:\olk.exe', 'Inbox', 0);
                """);
        }

        // A future script, appended the way a real one would be.
        IReadOnlyList<Migration> later =
        [
            .. Migrator.Embedded,
            new(Migrator.LatestVersion + 1, "9999_add_color.sql", "ALTER TABLE apps ADD COLUMN color TEXT NULL;"),
        ];

        using var database = _temp.Open(later);

        Version(database).ShouldBe(later.Count);
        using var check = database.Connect();
        check.QuerySingle<(string Key, string? Color)>("SELECT key, color FROM apps;").ShouldBe(("outlook", null));
        check.ExecuteScalar<string>("SELECT window_title FROM intervals;").ShouldBe("Inbox");
    }

    [Fact]
    public void A_backup_is_made_before_a_migration_runs()
    {
        using (var v1 = _temp.Open(Migrator.Embedded.Take(1).ToList()))
        {
            v1.BackupPath.ShouldBeNull(); // a new file has nothing to keep
            using var connection = v1.Connect();
            connection.Execute("""
                INSERT INTO intervals (id, start_utc, end_utc, kind, window_title, is_open)
                VALUES ('0190f2a0-0000-7000-8000-000000000001', 1000, 61000, 1, 'Inbox', 0);
                """);
        }

        // A migration that would lose the data if the backup were taken after it.
        IReadOnlyList<Migration> later =
        [
            .. Migrator.Embedded,
            new(Migrator.LatestVersion + 1, "9999_drop.sql", "DELETE FROM intervals;"),
        ];

        using var database = _temp.Open(later);

        var backup = database.BackupPath.ShouldNotBeNull();
        backup.ShouldBe(_temp.Path + ".v1.bak", StringCompareShould.IgnoreCase);
        using var copy = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={backup};Pooling=False");
        copy.Open();
        Migrator.GetVersion(copy).ShouldBe(1);
        copy.ExecuteScalar<string>("SELECT window_title FROM intervals;").ShouldBe("Inbox");
    }

    [Fact]
    public void No_backup_when_the_schema_is_up_to_date()
    {
        using (_temp.Open())
        {
        }

        using var again = _temp.Open();

        again.BackupPath.ShouldBeNull();
        Directory.GetFiles(Path.GetDirectoryName(_temp.Path)!, "*.bak").ShouldBeEmpty();
    }

    [Fact]
    public void Only_the_newest_backups_are_kept()
    {
        for (var version = 1; version <= Migrator.LatestVersion + 3; version++)
        {
            IReadOnlyList<Migration> upTo =
            [
                .. Migrator.Embedded.Take(version),
                .. Enumerable.Range(Migrator.LatestVersion + 1, Math.Max(0, version - Migrator.LatestVersion))
                    .Select(v => new Migration(v, $"{v:D4}_noop.sql", "SELECT 1;")),
            ];
            using (_temp.Open(upTo))
            {
            }
        }

        Directory.GetFiles(Path.GetDirectoryName(_temp.Path)!, "*.bak")
            .Select(Path.GetFileName)
            .Order(StringComparer.Ordinal)
            .ShouldBe(Enumerable.Range(Migrator.LatestVersion, TimeglassDatabase.BackupsKept)
                .Select(v => $"timeglass.db.v{v}.bak")
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public void A_failing_migration_is_rolled_back_completely()
    {
        using (_temp.Open(Migrator.Embedded.Take(1).ToList()))
        {
        }

        IReadOnlyList<Migration> broken =
        [
            .. Migrator.Embedded.Take(1),
            new(2, "0002_broken.sql", "CREATE TABLE half_done (x INTEGER); SELECT nope FROM missing;"),
        ];

        Should.Throw<SqliteException>(() => _temp.Open(broken));

        using var database = _temp.Open(Migrator.Embedded.Take(1).ToList());
        Version(database).ShouldBe(1);
        using var connection = database.Connect();
        connection.ExecuteScalar<long>("SELECT count(*) FROM sqlite_master WHERE name = 'half_done';").ShouldBe(0);
    }

    [Fact]
    public void A_database_from_a_newer_version_is_refused()
    {
        using (var database = _temp.Open())
        {
            using var connection = database.Connect();
            connection.Execute("PRAGMA user_version = 99;");
        }

        Should.Throw<InvalidOperationException>(() => _temp.Open()).Message.ShouldContain("newer version");
    }

    [Fact]
    public void Scripts_must_be_numbered_in_order()
    {
        using var database = _temp.Open();
        using var connection = database.Connect();

        Should.Throw<InvalidOperationException>(() =>
            Migrator.Apply(connection, [new(1, "a", ""), new(3, "c", "")]));
    }

    [Fact]
    public void Connections_use_wal_normal_sync_and_foreign_keys()
    {
        using var database = _temp.Open();
        using var connection = database.Connect();

        connection.ExecuteScalar<string>("PRAGMA journal_mode;").ShouldBe("wal");
        connection.ExecuteScalar<long>("PRAGMA synchronous;").ShouldBe(1); // NORMAL
        connection.ExecuteScalar<long>("PRAGMA foreign_keys;").ShouldBe(1);
    }

    [Fact]
    public void Open_creates_the_folder()
    {
        File.Exists(_temp.Path).ShouldBeFalse();

        using var database = _temp.Open();

        File.Exists(_temp.Path).ShouldBeTrue();
        database.Path.ShouldBe(Path.GetFullPath(_temp.Path));
    }

    [Fact]
    public void Default_path_is_under_local_app_data()
    {
        TimeglassDatabase.DefaultPath.ShouldEndWith(Path.Combine("Timeglass", "timeglass.db"));
    }
}
