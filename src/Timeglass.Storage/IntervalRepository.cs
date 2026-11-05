using System.Threading.Channels;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Timeglass.Core.Domain;
using Timeglass.Core.Ports;
using Timeglass.Core.Reporting;

namespace Timeglass.Storage;

/// <summary>
/// Stores intervals in SQLite and reads them back by time range.
/// </summary>
/// <remarks>
/// <para>
/// Writes go through a single background writer: <see cref="ApplyAsync"/> only queues the changes,
/// so the tracker never waits for the disk. Whatever has queued up since the last write is written
/// in one transaction. <see cref="FlushAsync"/> waits until everything queued so far is stored.
/// </para>
/// <para>
/// A failed write is logged and dropped. Every change carries the full interval, so the next
/// checkpoint of an open interval repairs it.
/// </para>
/// <para>
/// At most one interval is open (enforced by a unique index). Storing an open interval closes
/// any other one at its last checkpoint first.
/// </para>
/// <para>
/// <see cref="DeleteRangeAsync"/> goes through the same writer, so it is ordered with the
/// changes queued before it. It runs with <c>secure_delete</c> on, so deleted titles are
/// overwritten in the file rather than left in free pages.
/// </para>
/// </remarks>
public sealed class IntervalRepository : IIntervalSink, IAsyncDisposable
{
    private const string UpsertApp = """
        INSERT INTO apps (key, display_name) VALUES (@Key, @DisplayName)
        ON CONFLICT (key) DO UPDATE SET display_name = excluded.display_name
        RETURNING id;
        """;

    private const string UpsertInterval = """
        INSERT INTO intervals (id, start_utc, end_utc, kind, app_id, exe_path, window_title, is_open)
        VALUES (@Id, @StartUtc, @EndUtc, @Kind, @AppId, @ExePath, @Title, @IsOpen)
        ON CONFLICT (id) DO UPDATE SET
            start_utc = excluded.start_utc,
            end_utc = excluded.end_utc,
            kind = excluded.kind,
            app_id = excluded.app_id,
            exe_path = excluded.exe_path,
            window_title = excluded.window_title,
            is_open = excluded.is_open;
        """;

    private const string CloseOthers = """
        UPDATE intervals SET is_open = 0 WHERE is_open = 1 AND id <> @Id;
        """;

    private const string SelectRange = """
        SELECT i.id AS Id, i.start_utc AS StartUtc, i.end_utc AS EndUtc, i.kind AS Kind, i.is_open AS IsOpen,
               i.exe_path AS ExePath, i.window_title AS Title, a.key AS AppKey, a.display_name AS AppName
        FROM intervals i
        LEFT JOIN apps a ON a.id = i.app_id
        WHERE i.start_utc < @To AND (i.end_utc > @From OR i.is_open = 1)
        ORDER BY i.start_utc, i.end_utc;
        """;

    private const string SelectOverlapping = """
        SELECT i.id AS Id, i.start_utc AS StartUtc, i.end_utc AS EndUtc, i.kind AS Kind, i.is_open AS IsOpen,
               i.exe_path AS ExePath, i.window_title AS Title
        FROM intervals i
        WHERE i.is_open = 0 AND i.start_utc < @To AND (i.end_utc > @From OR i.start_utc >= @From);
        """;

    private const string DeleteInterval = "DELETE FROM intervals WHERE id = @Id;";

    private const string UpdateBounds = "UPDATE intervals SET start_utc = @StartUtc, end_utc = @EndUtc WHERE id = @Id;";

    private const string InsertPiece = """
        INSERT INTO intervals (id, start_utc, end_utc, kind, app_id, exe_path, window_title, is_open)
        SELECT @NewId, @StartUtc, @EndUtc, kind, app_id, exe_path, window_title, 0 FROM intervals WHERE id = @Id;
        """;

    private readonly TimeglassDatabase _database;
    private readonly ILogger _logger;
    private readonly Channel<WriteItem> _queue = Channel.CreateUnbounded<WriteItem>(new UnboundedChannelOptions
    {
        SingleReader = true,
        SingleWriter = false,
    });

    private readonly Dictionary<AppIdentity, long> _appIds = [];
    private readonly Task _writer;

    // Only touched by the writer loop.
    private Exception? _lastError;

    /// <summary>Creates the repository and starts its background writer.</summary>
    public IntervalRepository(TimeglassDatabase database, ILogger<IntervalRepository>? logger = null)
    {
        _database = database;
        _logger = logger ?? NullLogger<IntervalRepository>.Instance;
        _writer = Task.Run(WriteLoopAsync);
    }

    /// <summary>Queues changes for the background writer and returns immediately.</summary>
    /// <exception cref="ObjectDisposedException">The repository has been disposed.</exception>
    public ValueTask ApplyAsync(IReadOnlyList<IntervalChange> changes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        Enqueue(new WriteItem(changes, Flushed: null));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Deletes [<paramref name="from"/>, <paramref name="to"/>) after the changes queued so far,
    /// cutting intervals that overlap its edges (<see cref="IntervalRange.Delete"/>). The open
    /// interval is not touched. Completes once the deletion is stored; throws if it failed.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="to"/> is before <paramref name="from"/>.</exception>
    public async ValueTask DeleteRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken)
    {
        if (to < from)
        {
            throw new ArgumentException("The range must not end before it starts.", nameof(to));
        }

        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new WriteItem(Changes: null, done, new DeleteRange(from, to)));
        await done.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Completes once every change queued before this call has been written.
    /// Throws the error of the most recent write if that write failed.
    /// </summary>
    public async ValueTask FlushAsync(CancellationToken cancellationToken)
    {
        var flushed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(new WriteItem(Changes: null, flushed));
        await flushed.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Returns intervals that overlap [<paramref name="from"/>, <paramref name="to"/>), ordered by start,
    /// including ones that start before the range. Open intervals are included if they start before
    /// <paramref name="to"/>, since they are still running. Intervals are not clipped to the range.
    /// </summary>
    public async Task<IReadOnlyList<ActivityInterval>> GetRangeAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        await using var connection = _database.Connect();
        var rows = await connection.QueryAsync<IntervalRow>(new CommandDefinition(
            SelectRange,
            new { From = from.ToUnixTimeMilliseconds(), To = to.ToUnixTimeMilliseconds() },
            cancellationToken: cancellationToken)).ConfigureAwait(false);

        return [.. rows.Select(r => r.ToInterval())];
    }

    /// <summary>
    /// Where the time went on local day <paramref name="day"/> in <paramref name="zone"/>: active
    /// and away totals, first and last activity, and time and share per app
    /// (<see cref="DaySummaries.Build"/>). Intervals crossing the day's edges are clipped.
    /// </summary>
    /// <param name="day">The local date.</param>
    /// <param name="zone">The user's time zone.</param>
    /// <param name="now">The current time; the open interval counts as running until then.</param>
    /// <param name="dayStart">Local time the day begins, for people who work past midnight. Midnight by default.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    public async Task<DaySummary> GetDaySummaryAsync(
        DateOnly day,
        TimeZoneInfo zone,
        DateTimeOffset now,
        TimeSpan dayStart = default,
        CancellationToken cancellationToken = default)
    {
        var (from, to) = DaySummaries.Bounds(day, zone, dayStart);
        var intervals = await GetRangeAsync(from, to, cancellationToken).ConfigureAwait(false);
        return DaySummaries.Build(intervals, day, zone, now, dayStart);
    }

    /// <summary>Writes everything still queued, then stops the writer.</summary>
    public async ValueTask DisposeAsync()
    {
        _queue.Writer.TryComplete();
        await _writer.ConfigureAwait(false);
    }

    private void Enqueue(WriteItem item)
    {
        if (!_queue.Writer.TryWrite(item))
        {
            throw new ObjectDisposedException(nameof(IntervalRepository));
        }
    }

    private async Task WriteLoopAsync()
    {
        var batch = new List<WriteItem>();
        while (await _queue.Reader.WaitToReadAsync().ConfigureAwait(false))
        {
            batch.Clear();
            while (_queue.Reader.TryRead(out var item))
            {
                batch.Add(item);
            }

            if (batch.Any(i => i.Changes is not null || i.Delete is not null))
            {
                try
                {
                    Write(batch);
                    _lastError = null;
                }
#pragma warning disable CA1031 // The writer must survive a failed write; the error goes to the log and to flushers.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    _lastError = ex;
                    _appIds.Clear(); // ids from a rolled-back transaction are not real
                    _logger.LogError(ex, "Failed to write {Count} interval change(s)", batch.Sum(i => i.Changes?.Count ?? 0));
                }
            }

            foreach (var item in batch)
            {
                if (_lastError is null)
                {
                    item.Flushed?.TrySetResult();
                }
                else
                {
                    item.Flushed?.TrySetException(_lastError);
                }
            }
        }
    }

    private void Write(List<WriteItem> batch)
    {
        using var connection = _database.Connect();
        if (batch.Any(i => i.Delete is not null))
        {
            // Per connection, and not allowed inside a transaction.
            connection.Execute("PRAGMA secure_delete = ON;");
        }

        try
        {
            using var transaction = connection.BeginTransaction();
            foreach (var item in batch)
            {
                if (item.Changes is not null)
                {
                    WriteChanges(connection, transaction, item.Changes);
                }

                if (item.Delete is { } delete)
                {
                    WriteDelete(connection, transaction, delete);
                }
            }

            transaction.Commit();
        }
        finally
        {
            if (batch.Any(i => i.Delete is not null))
            {
                // Connections are pooled; don't leave the setting behind.
                connection.Execute("PRAGMA secure_delete = OFF;");
            }
        }
    }

    private static void WriteDelete(SqliteConnection connection, SqliteTransaction transaction, DeleteRange delete)
    {
        var rows = connection.Query<IntervalRow>(SelectOverlapping, new
        {
            From = delete.From.ToUnixTimeMilliseconds(),
            To = delete.To.ToUnixTimeMilliseconds(),
        }, transaction).ToList();

        foreach (var row in rows)
        {
            var pieces = IntervalRange.Delete(row.ToInterval(), delete.From, delete.To, Guid.CreateVersion7);
            var id = row.Id;
            var keepsRow = pieces.Count > 0 && pieces[0].Id.ToString("D") == id;
            foreach (var piece in pieces.Skip(keepsRow ? 1 : 0))
            {
                connection.Execute(InsertPiece, new
                {
                    NewId = piece.Id.ToString("D"),
                    Id = id,
                    StartUtc = piece.Start.ToUnixTimeMilliseconds(),
                    EndUtc = piece.End.ToUnixTimeMilliseconds(),
                }, transaction);
            }

            if (keepsRow)
            {
                connection.Execute(UpdateBounds, new
                {
                    Id = id,
                    StartUtc = pieces[0].Start.ToUnixTimeMilliseconds(),
                    EndUtc = pieces[0].End.ToUnixTimeMilliseconds(),
                }, transaction);
            }
            else
            {
                connection.Execute(DeleteInterval, new { Id = id }, transaction);
            }
        }
    }

    private void WriteChanges(SqliteConnection connection, SqliteTransaction transaction, IReadOnlyList<IntervalChange> changes)
    {
        foreach (var change in changes)
        {
            var interval = change.Interval;
            var id = interval.Id.ToString("D");

            if (interval.IsOpen)
            {
                // Only one interval can be open. If the write that closed the previous one was lost,
                // it ends at its last checkpoint, the same as after a crash.
                connection.Execute(CloseOthers, new { Id = id }, transaction);
            }

            connection.Execute(UpsertInterval, new
            {
                Id = id,
                StartUtc = interval.Start.ToUnixTimeMilliseconds(),
                EndUtc = interval.End.ToUnixTimeMilliseconds(),
                Kind = (int)interval.Kind,
                AppId = interval.App is null ? (long?)null : GetAppId(connection, transaction, interval.App),
                interval.ExePath,
                interval.Title,
                IsOpen = interval.IsOpen ? 1 : 0,
            }, transaction);
        }
    }

    private long GetAppId(SqliteConnection connection, SqliteTransaction transaction, AppIdentity app)
    {
        if (!_appIds.TryGetValue(app, out var id))
        {
            id = connection.ExecuteScalar<long>(UpsertApp, new { app.Key, app.DisplayName }, transaction);
            _appIds[app] = id;
        }

        return id;
    }

    private sealed record WriteItem(IReadOnlyList<IntervalChange>? Changes, TaskCompletionSource? Flushed, DeleteRange? Delete = null);

    private sealed record DeleteRange(DateTimeOffset From, DateTimeOffset To);

    private sealed class IntervalRow
    {
        public string Id { get; init; } = "";

        public long StartUtc { get; init; }

        public long EndUtc { get; init; }

        public long Kind { get; init; }

        public long IsOpen { get; init; }

        public string? ExePath { get; init; }

        public string? Title { get; init; }

        public string? AppKey { get; init; }

        public string? AppName { get; init; }

        public ActivityInterval ToInterval() => new(
            Id: Guid.Parse(Id),
            Kind: (IntervalKind)Kind,
            Start: DateTimeOffset.FromUnixTimeMilliseconds(StartUtc),
            End: DateTimeOffset.FromUnixTimeMilliseconds(EndUtc),
            IsOpen: IsOpen != 0,
            App: AppKey is null ? null : new AppIdentity(AppKey, AppName ?? AppKey),
            ExePath: ExePath,
            Title: Title);
    }
}
