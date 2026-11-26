using Timeglass.Core.Domain;
using Timeglass.Core.Reporting;
using Timeglass.Core.Settings;

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

    /// <summary>Timeline split at keyword-group boundaries.</summary>
    Task<DayTimeline> GetGroupedTimelineAsync(
        DateOnly day, TimeZoneInfo zone, DateTimeOffset now,
        IReadOnlyList<KeywordGroupSetting> groups, CancellationToken cancellationToken = default);

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
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<TitleBreakdown> GetTitleBreakdownAsync(
        string appKey,
        DateOnly firstDay,
        DateOnly lastDay,
        TimeZoneInfo zone,
        DateTimeOffset now,
        TimeSpan dayStart = default,
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
    /// <param name="cancellationToken">Cancels the query.</param>
    Task<RangeSummary> GetRangeSummaryAsync(
        DateOnly firstDay,
        DateOnly lastDay,
        TimeZoneInfo zone,
        DateTimeOffset now,
        TimeSpan dayStart = default,
        CancellationToken cancellationToken = default);

    /// <summary>Totals per day and keyword group over a range of local days.</summary>
    Task<RangeSummary> GetGroupedRangeSummaryAsync(
        DateOnly firstDay, DateOnly lastDay, TimeZoneInfo zone, DateTimeOffset now,
        IReadOnlyList<KeywordGroupSetting> groups, CancellationToken cancellationToken = default);

    /// <summary>Applications contributing to one keyword group over a range of local days.</summary>
    Task<RangeSummary> GetGroupApplicationsAsync(
        DateOnly firstDay, DateOnly lastDay, TimeZoneInfo zone, DateTimeOffset now,
        IReadOnlyList<KeywordGroupSetting> groups, string groupName, CancellationToken cancellationToken = default);

    /// <summary>Titles for an application's time assigned to a keyword group over a range.</summary>
    Task<TitleBreakdown> GetGroupApplicationTitlesAsync(
        DateOnly firstDay, DateOnly lastDay, TimeZoneInfo zone, DateTimeOffset now,
        IReadOnlyList<KeywordGroupSetting> groups, string groupName, string appKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The intervals that overlap [<paramref name="from"/>, <paramref name="to"/>), ordered by
    /// start and not clipped, read one at a time as they are asked for. For exports
    /// (<see cref="Exports"/>), where a range can hold more than should be loaded at once.
    /// </summary>
    /// <param name="from">Start of the range (inclusive).</param>
    /// <param name="to">End of the range (exclusive).</param>
    /// <param name="cancellationToken">Stops the query.</param>
    IAsyncEnumerable<ActivityInterval> StreamRangeAsync(
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
