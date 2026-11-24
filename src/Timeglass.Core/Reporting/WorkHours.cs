namespace Timeglass.Core.Reporting;

/// <summary>
/// The part of the day that counts as work hours (#110), as local times of day: by default
/// 09:00 to 17:00.
/// </summary>
/// <param name="Start">Local time work hours begin.</param>
/// <param name="End">Local time work hours end; after <paramref name="Start"/>.</param>
public sealed record WorkHours(TimeSpan Start, TimeSpan End)
{
    /// <summary>09:00 to 17:00.</summary>
    public static WorkHours Default { get; } = new(TimeSpan.FromHours(9), TimeSpan.FromHours(17));

    /// <summary>
    /// How much of the span from <paramref name="first"/> to <paramref name="last"/> activity falls
    /// inside work hours on local day <paramref name="day"/>: the workday from first to last
    /// activity, without the time before work hours begin or after they end. Zero when there was no
    /// activity or none of it overlaps work hours.
    /// </summary>
    public TimeSpan Within(DateOnly day, DateTimeOffset? first, DateTimeOffset? last, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        if (first is not { } from || last is not { } to)
        {
            return TimeSpan.Zero;
        }

        var midnight = day.ToDateTime(TimeOnly.MinValue);
        var workFrom = DaySummaries.FirstInstantAtOrAfter(midnight + Start, zone);
        var workTo = DaySummaries.FirstInstantAtOrAfter(midnight + End, zone);
        var overlapFrom = from > workFrom ? from : workFrom;
        var overlapTo = to < workTo ? to : workTo;
        return overlapTo > overlapFrom ? overlapTo - overlapFrom : TimeSpan.Zero;
    }
}
