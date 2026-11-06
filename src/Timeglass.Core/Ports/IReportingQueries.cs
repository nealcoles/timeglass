using Timeglass.Core.Reporting;

namespace Timeglass.Core.Ports;

/// <summary>
/// The reporting queries over stored intervals, for the API. Implemented by storage
/// (<c>IntervalRepository</c>); each method runs one range query and hands the rows to the
/// matching builder in <see cref="Reporting"/>.
/// </summary>
public interface IReportingQueries
{
    /// <summary>Where the time went on one local day; see <see cref="DaySummaries.Build"/>.</summary>
    /// <param name="day">The local date.</param>
    /// <param name="zone">The user's time zone.</param>
    /// <param name="now">The current time; the open interval counts as running until then.</param>
    /// <param name="dayStart">Local time the day begins. Midnight by default.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<DaySummary> GetDaySummaryAsync(
        DateOnly day,
        TimeZoneInfo zone,
        DateTimeOffset now,
        TimeSpan dayStart = default,
        CancellationToken cancellationToken = default);

    /// <summary>Active and away blocks of one local day, in time order; see <see cref="Timelines.ForDay"/>.</summary>
    /// <param name="day">The local date.</param>
    /// <param name="zone">The user's time zone.</param>
    /// <param name="now">The current time; the open interval counts as running until then.</param>
    /// <param name="dayStart">Local time the day begins. Midnight by default.</param>
    /// <param name="mergeGap">Merge gaps shorter than this; <see cref="Timelines.DefaultMergeGap"/> if not given.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<DayTimeline> GetTimelineAsync(
        DateOnly day,
        TimeZoneInfo zone,
        DateTimeOffset now,
        TimeSpan dayStart = default,
        TimeSpan? mergeGap = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// One app's time by window title over local days <paramref name="firstDay"/> to
    /// <paramref name="lastDay"/>, both included; see <see cref="TitleBreakdowns.Build"/>.
    /// </summary>
    /// <param name="appKey">The app's key, e.g. <c>outlook</c>.</param>
    /// <param name="firstDay">First local date.</param>
    /// <param name="lastDay">Last local date, included.</param>
    /// <param name="zone">The user's time zone.</param>
    /// <param name="now">The current time; the open interval counts as running until then.</param>
    /// <param name="dayStart">Local time each day begins. Midnight by default.</param>
    /// <param name="top">How many titles to list by name.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<TitleBreakdown> GetTitleBreakdownAsync(
        string appKey,
        DateOnly firstDay,
        DateOnly lastDay,
        TimeZoneInfo zone,
        DateTimeOffset now,
        TimeSpan dayStart = default,
        int top = TitleBreakdowns.DefaultTop,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Totals per day and per app over local days <paramref name="firstDay"/> to
    /// <paramref name="lastDay"/>, both included; see <see cref="RangeSummaries.Build"/>.
    /// </summary>
    /// <param name="firstDay">First local date.</param>
    /// <param name="lastDay">Last local date, included. At most <see cref="RangeSummaries.MaxDays"/> days after the first.</param>
    /// <param name="zone">The user's time zone.</param>
    /// <param name="now">The current time; the open interval counts as running until then.</param>
    /// <param name="dayStart">Local time each day begins. Midnight by default.</param>
    /// <param name="topApps">How many apps to list per day.</param>
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<RangeSummary> GetRangeSummaryAsync(
        DateOnly firstDay,
        DateOnly lastDay,
        TimeZoneInfo zone,
        DateTimeOffset now,
        TimeSpan dayStart = default,
        int topApps = RangeSummaries.DefaultTopApps,
        CancellationToken cancellationToken = default);
}
