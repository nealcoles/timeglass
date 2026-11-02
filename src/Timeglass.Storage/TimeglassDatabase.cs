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
    /// Opens (creating it and its folder if needed) the database at <paramref name="path"/>,
    /// applies pending migrations and closes intervals left open by a crash (see <see cref="RecoveredIntervals"/>).
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
            Migrator.Apply(connection, migrations ?? Migrator.Embedded);

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

    /// <summary>Closes pooled connections so the file is released.</summary>
    public void Dispose()
    {
        using var connection = new SqliteConnection(_connectionString);
        SqliteConnection.ClearPool(connection);
    }
}
