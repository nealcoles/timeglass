using Timeglass.Core.Domain;

namespace Timeglass.Core.Reporting;

/// <summary>Small helpers shared by the reporting builders.</summary>
internal static class Clipping
{
    /// <summary>
    /// Where <paramref name="interval"/> ends for reporting: an open interval runs until
    /// <paramref name="now"/>, but never ends before its last stored checkpoint.
    /// </summary>
    public static DateTimeOffset EffectiveEnd(ActivityInterval interval, DateTimeOffset now) =>
        interval.IsOpen && now > interval.End ? now : interval.End;

    public static DateTimeOffset Max(DateTimeOffset a, DateTimeOffset b) => a > b ? a : b;

    public static DateTimeOffset Min(DateTimeOffset a, DateTimeOffset b) => a < b ? a : b;

    public static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;
}
