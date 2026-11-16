using Timeglass.Core.Domain;

namespace Timeglass.Core.Reporting;

/// <summary>Builds <see cref="DaySummary"/> values from stored intervals.</summary>
/// <remarks>
/// Intervals are stored in UTC. A day is a local day in the given time zone, optionally starting
/// later than midnight (<c>dayStart</c>) for people who work past it. Intervals that cross a day
/// boundary are clipped, so each part counts towards its own day.
/// </remarks>
public static class DaySummaries
{
    /// <summary>The latest a day can start: a "day start" of 06:00 or more would be a different day.</summary>
    public static readonly TimeSpan MaxDayStart = TimeSpan.FromHours(6);

    /// <summary>
    /// The instants [start, end) of local day <paramref name="day"/> in <paramref name="zone"/>, where
    /// a day begins at <paramref name="dayStart"/> local time (midnight by default) and ends where the
    /// next one begins.
    /// </summary>
    /// <remarks>
    /// If the start time doesn't exist that day (clocks jumped past it), the day starts when the
    /// clocks jump. If it happens twice (clocks went back), the day starts at the first of the two.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="dayStart"/> is negative or not less than <see cref="MaxDayStart"/>.
    /// </exception>
    public static (DateTimeOffset Start, DateTimeOffset End) Bounds(DateOnly day, TimeZoneInfo zone, TimeSpan dayStart = default)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (dayStart < TimeSpan.Zero || dayStart >= MaxDayStart)
        {
            throw new ArgumentOutOfRangeException(nameof(dayStart), dayStart, $"The day must start between 00:00 and {MaxDayStart:hh\\:mm}.");
        }

        return (
            FirstInstantAtOrAfter(day.ToDateTime(TimeOnly.MinValue) + dayStart, zone),
            FirstInstantAtOrAfter(day.AddDays(1).ToDateTime(TimeOnly.MinValue) + dayStart, zone));
    }

    /// <summary>
    /// The local day that <paramref name="at"/> falls in, in <paramref name="zone"/>, when a day
    /// begins at <paramref name="dayStart"/>. With a day start of 04:00, 02:00 belongs to the day before.
    /// </summary>
    public static DateOnly DayOf(DateTimeOffset at, TimeZoneInfo zone, TimeSpan dayStart = default)
    {
        ArgumentNullException.ThrowIfNull(zone);
        var day = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, zone).DateTime);

        // Local times around a DST change don't map cleanly back, so compare instants.
        return at < Bounds(day, zone, dayStart).Start ? day.AddDays(-1) : day;
    }

    /// <summary>
    /// Summarises <paramref name="intervals"/> for local day <paramref name="day"/>. Intervals
    /// outside the day are ignored and ones crossing its edges are clipped. An open interval counts
    /// as running until <paramref name="now"/>.
    /// </summary>
    /// <param name="intervals">Stored intervals, for example from a range query over <see cref="Bounds"/>. Any order.</param>
    /// <param name="day">The local date.</param>
    /// <param name="zone">The user's time zone.</param>
    /// <param name="now">The current time, so a still-open interval has an end.</param>
    /// <param name="dayStart">Local time the day begins; see <see cref="Bounds"/>.</param>
    /// <param name="rolledUp">
    /// Totals that replaced intervals removed by retention (<see cref="DailyTotals"/>). Those for
    /// <paramref name="day"/> are added as they are; the rest are ignored.
    /// </param>
    public static DaySummary Build(
        IEnumerable<ActivityInterval> intervals,
        DateOnly day,
        TimeZoneInfo zone,
        DateTimeOffset now,
        TimeSpan dayStart = default,
        IEnumerable<RolledUpTotal>? rolledUp = null)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        var (dayFrom, dayTo) = Bounds(day, zone, dayStart);

        var active = TimeSpan.Zero;
        var away = TimeSpan.Zero;
        DateTimeOffset? first = null;
        DateTimeOffset? last = null;
        var perApp = new Dictionary<string, (AppIdentity App, DateTimeOffset LastSeen, TimeSpan Duration)>(StringComparer.Ordinal);

        foreach (var interval in intervals)
        {
            var end = interval.IsOpen && now > interval.End ? now : interval.End;
            var start = Max(interval.Start, dayFrom);
            end = Min(end, dayTo);
            if (end <= start)
            {
                continue;
            }

            var length = end - start;
            if (interval.Kind == IntervalKind.Away)
            {
                away += length;
                continue;
            }

            AddActive(interval.App, start, end, length);
        }

        foreach (var total in rolledUp ?? [])
        {
            if (total.Day != day)
            {
                continue;
            }

            if (total.Kind == IntervalKind.Away)
            {
                away += total.Duration;
            }
            else
            {
                AddActive(total.App, total.First, total.Last, total.Duration);
            }
        }

        void AddActive(AppIdentity? identity, DateTimeOffset start, DateTimeOffset end, TimeSpan length)
        {
            active += length;
            first = first is null ? start : Min(first.Value, start);
            last = last is null ? end : Max(last.Value, end);

            var app = identity ?? AppIdentity.Unknown;
            if (perApp.TryGetValue(app.Key, out var total))
            {
                // Display names can change (a rename in settings); show the latest one.
                perApp[app.Key] = (start > total.LastSeen ? app : total.App, Max(start, total.LastSeen), total.Duration + length);
            }
            else
            {
                perApp[app.Key] = (app, start, length);
            }
        }

        var apps = perApp.Values
            .OrderByDescending(a => a.Duration)
            .ThenBy(a => a.App.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.App.Key, StringComparer.Ordinal)
            .ToList();
        var percents = RoundToHundred([.. apps.Select(a => a.Duration.Ticks)]);
        var totals = apps
            .Select((a, i) => new AppDayTotal(a.App, a.Duration, (double)a.Duration.Ticks / active.Ticks, percents[i]))
            .ToList();

        return new DaySummary(day, dayFrom, dayTo, active, away, first, last, totals);
    }

    /// <summary>
    /// Whole percentages of <paramref name="parts"/> that add up to exactly 100 (largest remainder
    /// method): each part gets its percentage rounded down, then the points still missing go to the
    /// parts that lost the most by rounding, earlier parts first on a tie.
    /// </summary>
    /// <returns>One percentage per part; all zero if the parts sum to zero.</returns>
    public static IReadOnlyList<int> RoundToHundred(IReadOnlyList<long> parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        var sum = parts.Aggregate(Int128.Zero, (s, p) => s + (p < 0 ? throw new ArgumentException("Parts must not be negative.", nameof(parts)) : p));
        var result = new int[parts.Count];
        if (sum == 0)
        {
            return result;
        }

        var remainders = new Int128[parts.Count];
        var given = 0;
        for (var i = 0; i < parts.Count; i++)
        {
            var scaled = parts[i] * (Int128)100;
            result[i] = (int)(scaled / sum);
            remainders[i] = scaled % sum;
            given += result[i];
        }

        foreach (var i in Enumerable.Range(0, parts.Count).OrderByDescending(i => remainders[i]).ThenBy(i => i).Take(100 - given))
        {
            result[i]++;
        }

        return result;
    }

    /// <summary>The earliest instant whose local time in <paramref name="zone"/> is at or after <paramref name="local"/>.</summary>
    internal static DateTimeOffset FirstInstantAtOrAfter(DateTime local, TimeZoneInfo zone)
    {
        local = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        if (zone.IsInvalidTime(local))
        {
            // Inside a gap: the clocks jump at a whole minute, so the first valid minute is the jump.
            local = new DateTime(local.Ticks - (local.Ticks % TimeSpan.TicksPerMinute), DateTimeKind.Unspecified);
            while (zone.IsInvalidTime(local))
            {
                local = local.AddMinutes(1);
            }
        }

        var offset = zone.IsAmbiguousTime(local) ? zone.GetAmbiguousTimeOffsets(local).Max() : zone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset);
    }

    private static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    private static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;
}
