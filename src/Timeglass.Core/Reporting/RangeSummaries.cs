using Timeglass.Core.Domain;

namespace Timeglass.Core.Reporting;

/// <summary>Builds <see cref="RangeSummary"/> values: per-day totals over a range such as a week.</summary>
/// <remarks>Each day is summarised by <see cref="DaySummaries.Build"/>, so the days agree with the day view.</remarks>
public static class RangeSummaries
{
    /// <summary>How many apps each day lists unless the caller asks for another number.</summary>
    public const int DefaultTopApps = 3;

    /// <summary>The longest range a query may ask for, in days.</summary>
    public const int MaxDays = 366;

    /// <summary>
    /// The instants [start, end) from the start of local day <paramref name="firstDay"/> to the end
    /// of <paramref name="lastDay"/> in <paramref name="zone"/>; see <see cref="DaySummaries.Bounds"/>.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// <paramref name="lastDay"/> is before <paramref name="firstDay"/>, or the range is longer than <see cref="MaxDays"/>.
    /// </exception>
    public static (DateTimeOffset Start, DateTimeOffset End) Bounds(DateOnly firstDay, DateOnly lastDay, TimeZoneInfo zone, TimeSpan dayStart = default)
    {
        if (lastDay < firstDay)
        {
            throw new ArgumentException("The range must not end before it starts.", nameof(lastDay));
        }

        if (lastDay.DayNumber - firstDay.DayNumber + 1 > MaxDays)
        {
            throw new ArgumentException($"A range can be at most {MaxDays} days.", nameof(lastDay));
        }

        return (DaySummaries.Bounds(firstDay, zone, dayStart).Start, DaySummaries.Bounds(lastDay, zone, dayStart).End);
    }

    /// <summary>
    /// Summarises local days <paramref name="firstDay"/> to <paramref name="lastDay"/> (both
    /// included): totals and the top <paramref name="topApps"/> apps per day, and totals per app
    /// over the whole range. Intervals crossing a day boundary count towards each day they cover.
    /// An open interval counts as running until <paramref name="now"/>.
    /// </summary>
    /// <param name="intervals">Stored intervals, for example from a range query over <see cref="Bounds"/>. Any order.</param>
    /// <param name="firstDay">First local date.</param>
    /// <param name="lastDay">Last local date, included.</param>
    /// <param name="zone">The user's time zone.</param>
    /// <param name="now">The current time, so a still-open interval has an end.</param>
    /// <param name="dayStart">Local time each day begins; see <see cref="DaySummaries.Bounds"/>.</param>
    /// <param name="topApps">How many apps to list per day. 0 lists none.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="topApps"/> is negative.</exception>
    public static RangeSummary Build(
        IEnumerable<ActivityInterval> intervals,
        DateOnly firstDay,
        DateOnly lastDay,
        TimeZoneInfo zone,
        DateTimeOffset now,
        TimeSpan dayStart = default,
        int topApps = DefaultTopApps)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentOutOfRangeException.ThrowIfNegative(topApps);
        var (rangeStart, rangeEnd) = Bounds(firstDay, lastDay, zone, dayStart);

        // starts[i] is where day i begins; starts[^1] is the end of the range.
        var dayCount = lastDay.DayNumber - firstDay.DayNumber + 1;
        var starts = new DateTimeOffset[dayCount + 1];
        for (var i = 0; i < dayCount; i++)
        {
            starts[i] = DaySummaries.Bounds(firstDay.AddDays(i), zone, dayStart).Start;
        }

        starts[dayCount] = rangeEnd;

        // Hand each interval to the days it covers, so each day only looks at its own.
        var buckets = new List<ActivityInterval>[dayCount];
        foreach (var interval in intervals)
        {
            var start = Clipping.Max(interval.Start, rangeStart);
            var end = Clipping.Min(Clipping.EffectiveEnd(interval, now), rangeEnd);
            if (end <= start)
            {
                continue;
            }

            for (var i = DayIndex(starts, start); i < dayCount && starts[i] < end; i++)
            {
                (buckets[i] ??= []).Add(interval);
            }
        }

        var days = new List<DayTotal>(dayCount);
        var perApp = new Dictionary<string, (AppIdentity App, TimeSpan Duration)>(StringComparer.Ordinal);
        var active = TimeSpan.Zero;
        var away = TimeSpan.Zero;
        for (var i = 0; i < dayCount; i++)
        {
            var summary = DaySummaries.Build(buckets[i] ?? [], firstDay.AddDays(i), zone, now, dayStart);
            days.Add(new DayTotal(summary.Day, summary.Start, summary.End, summary.Active, summary.Away, [.. summary.Apps.Take(topApps)]));
            active += summary.Active;
            away += summary.Away;

            foreach (var app in summary.Apps)
            {
                // Days go forward, so a later day's display name replaces an earlier one.
                perApp[app.App.Key] = (app.App, perApp.GetValueOrDefault(app.App.Key).Duration + app.Duration);
            }
        }

        var apps = perApp.Values
            .OrderByDescending(a => a.Duration)
            .ThenBy(a => a.App.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.App.Key, StringComparer.Ordinal)
            .ToList();
        var percents = DaySummaries.RoundToHundred([.. apps.Select(a => a.Duration.Ticks)]);
        var totals = apps
            .Select((a, i) => new AppDayTotal(a.App, a.Duration, (double)a.Duration.Ticks / active.Ticks, percents[i]))
            .ToList();

        return new RangeSummary(firstDay, lastDay, rangeStart, rangeEnd, active, away, days, totals);
    }

    /// <summary>The index of the day that contains <paramref name="at"/>, which is inside the range.</summary>
    private static int DayIndex(DateTimeOffset[] starts, DateTimeOffset at)
    {
        // The last start that is at or before the instant.
        var index = Array.BinarySearch(starts, at);
        return index >= 0 ? index : ~index - 1;
    }
}
