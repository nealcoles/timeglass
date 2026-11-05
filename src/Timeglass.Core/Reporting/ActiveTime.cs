using Timeglass.Core.Domain;

namespace Timeglass.Core.Reporting;

/// <summary>Sums active time over a range, for quick totals such as the tray tooltip.</summary>
/// <remarks>
/// For a day's totals per app, with midnight splits, see <see cref="DaySummaries"/>;
/// this only answers "how long was I active between these two instants".
/// </remarks>
public static class ActiveTime
{
    /// <summary>
    /// Total length of the <see cref="IntervalKind.Active"/> intervals inside
    /// [<paramref name="from"/>, <paramref name="to"/>). Intervals are clipped to the range, and an
    /// open interval counts as running until <paramref name="to"/>.
    /// </summary>
    public static TimeSpan Total(IEnumerable<ActivityInterval> intervals, DateTimeOffset from, DateTimeOffset to)
    {
        ArgumentNullException.ThrowIfNull(intervals);

        var total = TimeSpan.Zero;
        foreach (var interval in intervals)
        {
            if (interval.Kind != IntervalKind.Active)
            {
                continue;
            }

            var start = interval.Start > from ? interval.Start : from;
            var end = interval.IsOpen ? to : interval.End < to ? interval.End : to;
            if (end > start)
            {
                total += end - start;
            }
        }

        return total;
    }

    /// <summary>
    /// The first instant of the local day that contains <paramref name="at"/> in <paramref name="zone"/>.
    /// Usually local midnight; where a DST change skips midnight, the first local time that exists,
    /// and where midnight happens twice, the first of the two.
    /// </summary>
    public static DateTimeOffset StartOfDay(DateTimeOffset at, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);

        return DaySummaries.FirstInstantAtOrAfter(TimeZoneInfo.ConvertTime(at, zone).Date, zone);
    }
}
