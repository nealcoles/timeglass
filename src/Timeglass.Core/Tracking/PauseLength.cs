namespace Timeglass.Core.Tracking;

/// <summary>How long the user pauses tracking for, as offered in the tray menu.</summary>
public enum PauseLength
{
    /// <summary>Until the user resumes.</summary>
    UntilResumed,

    /// <summary>30 minutes.</summary>
    ThirtyMinutes,

    /// <summary>One hour.</summary>
    OneHour,

    /// <summary>Until local midnight.</summary>
    UntilTomorrow,
}

/// <summary>Works out when a pause ends.</summary>
public static class PauseSchedule
{
    /// <summary>
    /// When a pause of <paramref name="length"/> that starts at <paramref name="now"/> ends, or
    /// <see langword="null"/> if it lasts until the user resumes.
    /// </summary>
    /// <param name="length">The length the user picked.</param>
    /// <param name="now">The current time.</param>
    /// <param name="zone">The user's time zone, which decides when tomorrow starts.</param>
    /// <remarks>
    /// <see cref="PauseLength.UntilTomorrow"/> ends at the next local midnight. Where a daylight
    /// saving change skips midnight, it ends at the first local time that exists after it.
    /// </remarks>
    public static DateTimeOffset? EndOf(PauseLength length, DateTimeOffset now, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return length switch
        {
            PauseLength.UntilResumed => null,
            PauseLength.ThirtyMinutes => now.AddMinutes(30),
            PauseLength.OneHour => now.AddHours(1),
            PauseLength.UntilTomorrow => NextMidnight(now, zone),
            _ => throw new ArgumentOutOfRangeException(nameof(length), length, null),
        };
    }

    private static DateTimeOffset NextMidnight(DateTimeOffset now, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now, zone);
        var midnight = DateTime.SpecifyKind(local.Date.AddDays(1), DateTimeKind.Unspecified);

        // A spring-forward gap at midnight: the day starts at the end of the gap.
        while (zone.IsInvalidTime(midnight))
        {
            midnight = midnight.AddMinutes(15);
        }

        // An ambiguous midnight (fall back) counts from its first occurrence.
        var offset = zone.IsAmbiguousTime(midnight)
            ? zone.GetAmbiguousTimeOffsets(midnight).Max()
            : zone.GetUtcOffset(midnight);
        return new DateTimeOffset(midnight, offset);
    }
}
