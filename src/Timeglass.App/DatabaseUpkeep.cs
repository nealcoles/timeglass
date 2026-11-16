using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Timeglass.Core.Ports;
using Timeglass.Core.Reporting;
using Timeglass.Storage;

namespace Timeglass.App;

/// <summary>
/// Keeps the database small and healthy over years of use (#41): once a day it applies the
/// <c>retentionMonths</c> setting and runs <see cref="DatabaseMaintenance"/>.
/// </summary>
/// <remarks>
/// <para>
/// The day is measured on the wall clock, looked at every <see cref="PollInterval"/>, so a run that
/// came due while the PC slept happens soon after it wakes.
/// </para>
/// <para>
/// A <c>VACUUM</c> rewrites the whole file and blocks the tracker's writes while it runs, so it
/// only runs while the user has been away for <see cref="IdleForVacuum"/>. If the daily run finds
/// the file worth rebuilding but the user is busy, a later look tries again.
/// </para>
/// </remarks>
internal sealed partial class DatabaseUpkeep : BackgroundService
{
    /// <summary>How long after start the first run happens, so it doesn't compete with startup.</summary>
    public static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(5);

    /// <summary>Time between two runs.</summary>
    public static readonly TimeSpan RunInterval = TimeSpan.FromHours(24);

    /// <summary>How often the service looks at the clock, and how soon a failed run is retried.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromHours(1);

    /// <summary>No input for this long, and a <c>VACUUM</c> may run.</summary>
    public static readonly TimeSpan IdleForVacuum = TimeSpan.FromMinutes(10);

    private readonly IntervalRepository _repository;
    private readonly IIdleSource _idle;
    private readonly Func<int?> _retentionMonths;
    private readonly TimeProvider _time;
    private readonly ILogger _logger;

    /// <param name="repository">The database.</param>
    /// <param name="idle">Tells whether the user is away, for <c>VACUUM</c>.</param>
    /// <param name="retentionMonths">The current <c>retentionMonths</c> setting; read before each run.</param>
    /// <param name="time">The clock and the local time zone.</param>
    /// <param name="logger">Where runs are logged.</param>
    public DatabaseUpkeep(
        IntervalRepository repository,
        IIdleSource idle,
        Func<int?> retentionMonths,
        TimeProvider time,
        ILogger<DatabaseUpkeep> logger)
    {
        _repository = repository;
        _idle = idle;
        _retentionMonths = retentionMonths;
        _time = time;
        _logger = logger;
    }

    /// <summary>
    /// Where retention cuts for <paramref name="months"/> months at <paramref name="now"/>: the
    /// start of the local day <paramref name="months"/> months before today.
    /// </summary>
    public static DateTimeOffset RetentionCutoff(DateTimeOffset now, TimeZoneInfo zone, int months)
    {
        var today = DaySummaries.DayOf(now, zone);
        return DaySummaries.Bounds(today.AddMonths(-months), zone).Start;
    }

    /// <summary>
    /// Applies retention (if it is set) and runs the maintenance once.
    /// </summary>
    /// <returns>Whether a <c>VACUUM</c> is still wanted because the user was not away.</returns>
    public async Task<bool> RunOnceAsync(CancellationToken cancellationToken)
    {
        if (_retentionMonths() is { } months)
        {
            var zone = _time.LocalTimeZone;
            var cutoff = RetentionCutoff(_time.GetUtcNow(), zone, months);
            var rolledUp = await _repository.ApplyRetentionAsync(cutoff, zone, cancellationToken).ConfigureAwait(false);
            if (rolledUp > 0)
            {
                LogRetention(_logger, rolledUp, cutoff, months);
            }
        }

        return await MaintainAsync(cancellationToken).ConfigureAwait(false);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartupDelay, _time, stoppingToken).ConfigureAwait(false);

            DateTimeOffset? lastRun = null;
            var vacuumWanted = false;
            while (true)
            {
                var now = _time.GetUtcNow();
                try
                {
                    if (lastRun is not { } last || now - last >= RunInterval)
                    {
                        vacuumWanted = await RunOnceAsync(stoppingToken).ConfigureAwait(false);
                        lastRun = now;
                    }
                    else if (vacuumWanted && IsAway())
                    {
                        vacuumWanted = await MaintainAsync(stoppingToken).ConfigureAwait(false);
                    }
                }
                // Disk full, file locked by a backup tool: try again at the next look. Tracking goes on.
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    LogFailed(_logger, ex);
                }

                await Task.Delay(PollInterval, _time, stoppingToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
    }

    private bool IsAway() => _time.GetUtcNow() - _idle.GetLastInputTime() >= IdleForVacuum;

    /// <returns>Whether a <c>VACUUM</c> is wanted but did not run.</returns>
    private async Task<bool> MaintainAsync(CancellationToken cancellationToken)
    {
        var result = await _repository.RunMaintenanceAsync(IsAway(), cancellationToken).ConfigureAwait(false);
        if (result.Vacuumed)
        {
            LogVacuumed(_logger, result.FreePages, result.Pages);
        }

        return !result.Vacuumed && DatabaseMaintenance.ShouldVacuum(result.FreePages, result.Pages);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Retention ({Months} months): rolled up {Count} interval(s) before {Cutoff:O} into daily totals")]
    private static partial void LogRetention(ILogger logger, int count, DateTimeOffset cutoff, int months);

    [LoggerMessage(Level = LogLevel.Information, Message = "Rebuilt the database file; {Free} of {Pages} pages were unused")]
    private static partial void LogVacuumed(ILogger logger, long free, long pages);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Database maintenance failed; trying again later")]
    private static partial void LogFailed(ILogger logger, Exception exception);
}
