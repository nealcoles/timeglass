using System.Globalization;
using Dapper;
using Microsoft.Data.Sqlite;
using Timeglass.Storage.Migrations;

namespace Timeglass.Storage;

/// <summary>
/// The Timeglass SQLite database file: opens connections with the right settings and keeps the
/// schema up to date.
/// </summary>
public sealed class TimeglassDatabase : IDisposable
{
    private readonly string _connectionString;

    private TimeglassDatabase(string path)
    {
        Path = path;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true,
        }.ToString();
    }

    /// <summary><c>%LOCALAPPDATA%\Timeglass\timeglass.db</c>.</summary>
    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Timeglass",
        "timeglass.db");

    /// <summary>Full path of the database file.</summary>
    public string Path { get; }

    /// <summary>
    /// How many intervals were still open when the database was opened, i.e. left behind by a crash
    /// or power loss. They were closed at their last checkpoint.
    /// </summary>
    public int RecoveredIntervals { get; private set; }

    /// <summary>
    /// The copy of the database made before <see cref="Open"/> migrated it to a newer schema, or
    /// <see langword="null"/> if there was nothing to migrate.
    /// </summary>
    public string? BackupPath { get; private set; }

    /// <summary>How many backups made before migrations are kept; older ones are deleted.</summary>
    public const int BackupsKept = 3;

    /// <summary>
    /// Opens (creating it and its folder if needed) the database at <paramref name="path"/>,
    /// applies pending migrations and closes intervals left open by a crash (see <see cref="RecoveredIntervals"/>).
    /// Before migrating an existing database it copies it next to itself as
    /// <c>timeglass.db.v{N}.bak</c>, where N is the schema version it had (see <see cref="BackupPath"/>).
    /// Call it before tracking starts.
    /// </summary>
    /// <param name="path">Database file; usually <see cref="DefaultPath"/>.</param>
    /// <param name="migrations">Scripts to apply; defaults to <see cref="Migrator.Embedded"/>. Tests pass subsets.</param>
    public static TimeglassDatabase Open(string path, IReadOnlyList<Migration>? migrations = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        var fullPath = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(fullPath)!);

        var database = new TimeglassDatabase(fullPath);
        try
        {
            using var connection = database.Connect();

            // WAL is a property of the file and can't be set inside a transaction, so set it once here.
            connection.Execute("PRAGMA journal_mode = WAL;");

            migrations ??= Migrator.Embedded;
            var version = Migrator.GetVersion(connection);
            if (version > 0 && version < migrations.Count)
            {
                database.BackupPath = Backup(connection, fullPath, version);
            }

            Migrator.Apply(connection, migrations);

            // Nothing is tracking yet, so anything open was never closed: its end is the last checkpoint.
            database.RecoveredIntervals = connection.Execute("UPDATE intervals SET is_open = 0 WHERE is_open = 1;");
        }
        catch
        {
            database.Dispose();
            throw;
        }

        return database;
    }

    /// <summary>
    /// Opens a connection with <c>synchronous=NORMAL</c> and <c>foreign_keys=ON</c>.
    /// Connections are pooled; dispose them after use.
    /// </summary>
    public SqliteConnection Connect()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            connection.Execute("""
                PRAGMA synchronous = NORMAL;
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 5000;
                """);
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Copies the database to <c>{path}.v{version}.bak</c> with SQLite's online backup, which
    /// includes what is still in the write-ahead log, and deletes all but the newest
    /// <see cref="BackupsKept"/> such copies.
    /// </summary>
    private static string Backup(SqliteConnection connection, string path, int version)
    {
        var backupPath = string.Create(CultureInfo.InvariantCulture, $"{path}.v{version}.bak");
        File.Delete(backupPath);

        var target = new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString();
        using (var backup = new SqliteConnection(target))
        {
            backup.Open();
            connection.BackupDatabase(backup);
        }

        var folder = System.IO.Path.GetDirectoryName(path)!;
        var prefix = System.IO.Path.GetFileName(path) + ".v";
        var old = Directory.EnumerateFiles(folder, prefix + "*.bak")
            .Select(f => (File: f, Version: int.TryParse(
                System.IO.Path.GetFileName(f)[prefix.Length..^".bak".Length],
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var v) ? v : -1))
            .Where(b => b.Version >= 0)
            .OrderByDescending(b => b.Version)
            .Skip(BackupsKept);
        foreach (var (file, _) in old)
        {
            File.Delete(file);
        }

        return backupPath;
    }

    /// <summary>Closes pooled connections so the file is released.</summary>
    public void Dispose()
    {
        using var connection = new SqliteConnection(_connectionString);
        SqliteConnection.ClearPool(connection);
    }
}
