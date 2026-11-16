using Timeglass.Core.Domain;

namespace Timeglass.Core.Reporting;

/// <summary>
/// The time of one kind in one app on one local day: what is kept of a day once retention has
/// deleted its intervals (and with them the window titles and the timeline).
/// </summary>
/// <param name="Day">The local date, in the time zone the PC was in when the day was rolled up.</param>
/// <param name="Kind">Active or away.</param>
/// <param name="App">The app for active time; <see langword="null"/> for away time and for active time without an app.</param>
/// <param name="Duration">Total time.</param>
/// <param name="First">Start of the earliest time counted.</param>
/// <param name="Last">End of the latest time counted.</param>
public sealed record RolledUpTotal(
    DateOnly Day,
    IntervalKind Kind,
    AppIdentity? App,
    TimeSpan Duration,
    DateTimeOffset First,
    DateTimeOffset Last);

/// <summary>Builds the <see cref="RolledUpTotal"/> values that replace intervals removed by retention.</summary>
public static class DailyTotals
{
    /// <summary>
    /// Sums the parts of <paramref name="intervals"/> that lie before <paramref name="before"/>,
    /// per local day, kind and app. An interval crossing a day boundary counts towards each day
    /// it covers, as in <see cref="DaySummaries.Build"/>, so a day summary built from the totals
    /// equals one built from the intervals.
    /// </summary>
    /// <param name="intervals">Closed intervals, read once. Fastest in start order.</param>
    /// <param name="before">Only time before this instant is counted; usually the start of a local day.</param>
    /// <param name="zone">The user's time zone.</param>
    /// <param name="dayStart">Local time each day begins; see <see cref="DaySummaries.Bounds"/>.</param>
    /// <returns>The totals, by day, then away after active, then app key.</returns>
    public static IReadOnlyList<RolledUpTotal> Build(
        IEnumerable<ActivityInterval> intervals,
        DateTimeOffset before,
        TimeZoneInfo zone,
        TimeSpan dayStart = default)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(zone);

        var totals = new Dictionary<(DateOnly Day, IntervalKind Kind, string? AppKey), RolledUpTotal>();

        // Intervals usually come in start order, so the day found for one mostly fits the next.
        var day = default(DateOnly);
        var (dayFrom, dayTo) = (DateTimeOffset.MaxValue, DateTimeOffset.MinValue);

        foreach (var interval in intervals)
        {
            var end = Clipping.Min(interval.End, before);
            var at = interval.Start;
            var app = interval.Kind == IntervalKind.Active ? interval.App : null;

            while (at < end)
            {
                if (at < dayFrom || at >= dayTo)
                {
                    day = DaySummaries.DayOf(at, zone, dayStart);
                    (dayFrom, dayTo) = DaySummaries.Bounds(day, zone, dayStart);
                }

                var pieceEnd = Clipping.Min(end, dayTo);
                var key = (day, interval.Kind, app?.Key);
                totals[key] = totals.TryGetValue(key, out var total)
                    ? total with
                    {
                        // Display names can change; keep the latest one.
                        App = at >= total.First ? app : total.App,
                        Duration = total.Duration + (pieceEnd - at),
                        First = Clipping.Min(total.First, at),
                        Last = Clipping.Max(total.Last, pieceEnd),
                    }
                    : new RolledUpTotal(day, interval.Kind, app, pieceEnd - at, at, pieceEnd);

                at = pieceEnd;
            }
        }

        return
        [
            .. totals.Values
                .OrderBy(t => t.Day)
                .ThenBy(t => t.Kind)
                .ThenBy(t => t.App?.Key, StringComparer.Ordinal),
        ];
    }
}
