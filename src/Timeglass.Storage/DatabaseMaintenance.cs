using Dapper;
using Microsoft.Data.Sqlite;

namespace Timeglass.Storage;

/// <summary>What a maintenance run did.</summary>
/// <param name="Vacuumed">Whether the file was rebuilt with <c>VACUUM</c>.</param>
/// <param name="FreePages">Unused pages in the file before the run.</param>
/// <param name="Pages">Pages in the file before the run.</param>
public sealed record MaintenanceResult(bool Vacuumed, long FreePages, long Pages);

/// <summary>The periodic upkeep of the database file (#41).</summary>
public static class DatabaseMaintenance
{
    /// <summary>A <c>VACUUM</c> is worth it once this share of the file is unused pages.</summary>
    public const double VacuumFreeShare = 0.25;

    /// <summary>
    /// And at least this many pages are unused (about 1 MB), so a small file is not rebuilt to
    /// win a few kilobytes.
    /// </summary>
    public const long VacuumMinFreePages = 256;

    /// <summary>
    /// Runs <c>PRAGMA optimize</c>, rebuilds the file with <c>VACUUM</c> if
    /// <paramref name="allowVacuum"/> and enough of it is unused (<see cref="ShouldVacuum"/>),
    /// and moves the write-ahead log into the database file, emptying it.
    /// </summary>
    /// <param name="connection">An open connection with no transaction; nothing else should be writing.</param>
    /// <param name="allowVacuum">
    /// Whether this is a good moment for a <c>VACUUM</c>, which rewrites the whole file and blocks
    /// writers meanwhile: pass true only while the user is away.
    /// </param>
    public static MaintenanceResult Run(SqliteConnection connection, bool allowVacuum)
    {
        ArgumentNullException.ThrowIfNull(connection);

        // Updates the query planner's statistics where SQLite thinks they are stale.
        connection.Execute("PRAGMA optimize;");

        var free = connection.ExecuteScalar<long>("PRAGMA freelist_count;");
        var pages = connection.ExecuteScalar<long>("PRAGMA page_count;");
        var vacuum = allowVacuum && ShouldVacuum(free, pages);
        if (vacuum)
        {
            connection.Execute("VACUUM;");
        }

        // Without this the log only shrinks when the last connection closes, which with pooled
        // connections is when the app quits.
        connection.Execute("PRAGMA wal_checkpoint(TRUNCATE);");
        return new MaintenanceResult(vacuum, free, pages);
    }

    /// <summary>Whether a file of <paramref name="pages"/> pages with <paramref name="freePages"/> unused is worth rebuilding.</summary>
    public static bool ShouldVacuum(long freePages, long pages) =>
        freePages >= VacuumMinFreePages && freePages >= pages * VacuumFreeShare;
}
