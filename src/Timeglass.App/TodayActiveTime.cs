using Timeglass.Core.Reporting;
using Timeglass.Storage;

namespace Timeglass.App;

/// <summary>Today's active time, for the tray tooltip.</summary>
internal sealed class TodayActiveTime
{
    private readonly IntervalRepository _intervals;
    private readonly TimeProvider _time;

    public TodayActiveTime(IntervalRepository intervals, TimeProvider time)
    {
        _intervals = intervals;
        _time = time;
    }

    /// <summary>Active time since local midnight, counting the open interval up to now.</summary>
    public async Task<TimeSpan> GetAsync(CancellationToken cancellationToken = default)
    {
        var now = _time.GetUtcNow();
        var start = ActiveTime.StartOfDay(now, _time.LocalTimeZone);
        var intervals = await _intervals.GetRangeAsync(start, now, cancellationToken).ConfigureAwait(false);
        return ActiveTime.Total(intervals, start, now);
    }
}
