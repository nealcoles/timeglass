using Timeglass.Core.Domain;

namespace Timeglass.Core.Reporting;

/// <summary>Options for excluding a short final return after a long absence.</summary>
public sealed record AfterHoursOptions(bool Enabled, WorkHours WorkHours, TimeSpan MinimumAway, TimeSpan MaximumReturn);

/// <summary>Excludes a short final return following a long absence after work hours.</summary>
public static class AfterHoursReturns
{
    /// <summary>
    /// Filters qualifying intervals without changing the stored records. Only a completed return
    /// with no later activity on the same local day can be identified as the end of the day.
    /// </summary>
    public static IReadOnlyList<ActivityInterval> Filter(
        IEnumerable<ActivityInterval> intervals,
        TimeZoneInfo zone,
        WorkHours hours,
        TimeSpan minimumAway,
        TimeSpan maximumReturn)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(zone);
        ArgumentNullException.ThrowIfNull(hours);
        var ordered = intervals.OrderBy(i => i.Start).ThenBy(i => i.End).ToArray();
        var excluded = new HashSet<Guid>();
        for (var i = 0; i + 1 < ordered.Length; i++)
        {
            var away = ordered[i];
            var first = ordered[i + 1];
            if (away.Kind != IntervalKind.Away || away.IsOpen || away.Duration < minimumAway
                || first.Kind != IntervalKind.Active || first.Start != away.End)
            {
                continue;
            }

            var day = DaySummaries.DayOf(first.Start, zone);
            var (_, dayEnd) = DaySummaries.Bounds(day, zone);
            var workEnd = DaySummaries.FirstInstantAtOrAfter(day.ToDateTime(TimeOnly.MinValue) + hours.End, zone);
            if (first.Start < workEnd)
            {
                continue;
            }

            var endIndex = i + 1;
            var lastEnd = first.End;
            var duration = first.Duration;
            var isOpen = first.IsOpen;
            while (endIndex + 1 < ordered.Length
                && ordered[endIndex + 1].Kind == IntervalKind.Active
                && ordered[endIndex + 1].Start == lastEnd)
            {
                var next = ordered[++endIndex];
                duration += next.Duration;
                lastEnd = next.End;
                isOpen |= next.IsOpen;
            }

            if (isOpen || duration <= TimeSpan.Zero || duration > maximumReturn || lastEnd > dayEnd
                || ordered.Skip(endIndex + 1).TakeWhile(x => x.Start < dayEnd).Any(x => x.Kind == IntervalKind.Active)
                || (endIndex + 1 < ordered.Length && ordered[endIndex + 1].Start == lastEnd))
            {
                continue;
            }

            excluded.Add(away.Id);
            for (var j = i + 1; j <= endIndex; j++)
            {
                excluded.Add(ordered[j].Id);
            }
        }

        return [.. ordered.Where(i => !excluded.Contains(i.Id))];
    }
}
