using System.Threading.Channels;
using Dapper;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Timeglass.Core.Domain;
using Timeglass.Core.Ports;

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

            var changes = batch.Where(i => i.Changes is not null).SelectMany(i => i.Changes!).ToList();
            if (changes.Count > 0)
            {
                try
                {
                    Write(changes);
                    _lastError = null;
                }
#pragma warning disable CA1031 // The writer must survive a failed write; the error goes to the log and to flushers.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    _lastError = ex;
                    _appIds.Clear(); // ids from a rolled-back transaction are not real
                    _logger.LogError(ex, "Failed to write {Count} interval change(s)", changes.Count);
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

    private void Write(List<IntervalChange> changes)
    {
        using var connection = _database.Connect();
        using var transaction = connection.BeginTransaction();

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

        transaction.Commit();
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

    private sealed record WriteItem(IReadOnlyList<IntervalChange>? Changes, TaskCompletionSource? Flushed);

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
