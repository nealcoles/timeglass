using Timeglass.Core.Domain;

namespace Timeglass.Core.Reporting;

/// <summary>Builds <see cref="RangeSummary"/> values: per-day totals over a range such as a week.</summary>
/// <remarks>Each day is summarised by <see cref="DaySummaries.Build"/>, so the days agree with the day view.</remarks>
public static class RangeSummaries
{
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
    /// included): totals and every app per day, and totals per app
    /// over the whole range. Intervals crossing a day boundary count towards each day they cover.
    /// An open interval counts as running until <paramref name="now"/>.
    /// </summary>
    /// <param name="intervals">Stored intervals, for example from a range query over <see cref="Bounds"/>. Any order.</param>
    /// <param name="firstDay">First local date.</param>
    /// <param name="lastDay">Last local date, included.</param>
    /// <param name="zone">The user's time zone.</param>
    /// <param name="now">The current time, so a still-open interval has an end.</param>
    /// <param name="dayStart">Local time each day begins; see <see cref="DaySummaries.Bounds"/>.</param>
    /// <param name="rolledUp">Totals that replaced intervals removed by retention; see <see cref="DaySummaries.Build"/>.</param>
    public static RangeSummary Build(
        IEnumerable<ActivityInterval> intervals,
        DateOnly firstDay,
        DateOnly lastDay,
        TimeZoneInfo zone,
        DateTimeOffset now,
        TimeSpan dayStart = default,
        IEnumerable<RolledUpTotal>? rolledUp = null)
    {
        ArgumentNullException.ThrowIfNull(intervals);
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

        var rolledUpByDay = (rolledUp ?? []).ToLookup(t => t.Day);
        var days = new List<DayTotal>(dayCount);
        var perApp = new Dictionary<string, (AppIdentity App, TimeSpan Duration, DateTimeOffset LastActivity)>(StringComparer.Ordinal);
        var active = TimeSpan.Zero;
        var away = TimeSpan.Zero;
        for (var i = 0; i < dayCount; i++)
        {
            var day = firstDay.AddDays(i);
            var summary = DaySummaries.Build(buckets[i] ?? [], day, zone, now, dayStart, rolledUpByDay[day]);
            days.Add(new DayTotal(summary.Day, summary.Start, summary.End, summary.Active, summary.Away, summary.Apps));
            active += summary.Active;
            away += summary.Away;

            foreach (var app in summary.Apps)
            {
                // Days go forward, so a later day's display name replaces an earlier one.
                var previous = perApp.GetValueOrDefault(app.App.Key);
                perApp[app.App.Key] = (app.App, previous.Duration + app.Duration, Clipping.Max(previous.LastActivity, app.LastActivity));
            }
        }

        var apps = perApp.Values
            .OrderByDescending(a => a.Duration)
            .ThenBy(a => a.App.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.App.Key, StringComparer.Ordinal)
            .ToList();
        var percents = DaySummaries.RoundToHundred([.. apps.Select(a => a.Duration.Ticks)]);
        var totals = apps
            .Select((a, i) => new AppDayTotal(a.App, a.Duration, (double)a.Duration.Ticks / active.Ticks, percents[i], a.LastActivity))
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
