using System.Text.Json.Serialization;
using Timeglass.Core.Reporting;

namespace Timeglass.Api;

/// <summary>What one row of an export is.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<ExportGranularity>))]
public enum ExportGranularity
{
    /// <summary>One row per recorded interval.</summary>
    [JsonStringEnumMemberName("intervals")]
    Intervals,

    /// <summary>One row per day and app, with the app's active time that day.</summary>
    [JsonStringEnumMemberName("daily-apps")]
    DailyApps,
}

/// <summary>The recorded time of a range of days, as a JSON export. One of the two lists is filled, by granularity.</summary>
/// <param name="From">First local date.</param>
/// <param name="To">Last local date (included).</param>
/// <param name="Granularity">Which of the lists is filled.</param>
/// <param name="Intervals">The intervals in time order, clipped to the range; null for daily-apps.</param>
/// <param name="DailyApps">Active time per day and app, in date order and the most time first within a day; null for intervals.</param>
public sealed record ExportDto(
    DateOnly From,
    DateOnly To,
    ExportGranularity Granularity,
    IAsyncEnumerable<ExportIntervalDto>? Intervals,
    IAsyncEnumerable<ExportDailyAppDto>? DailyApps);

/// <summary>One recorded interval.</summary>
/// <param name="Kind">Active or away.</param>
/// <param name="App">The app of active time; null for away time.</param>
/// <param name="Title">The window title; null if none was recorded or the privacy settings hide it.</param>
/// <param name="Start">Start (inclusive).</param>
/// <param name="End">End (exclusive); the time of the export if the interval is still running.</param>
/// <param name="DurationMs">End minus start, in milliseconds.</param>
/// <param name="IsOpen">True if the interval is still running.</param>
public sealed record ExportIntervalDto(
    ActivityKind Kind,
    AppDto? App,
    string? Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    long DurationMs,
    bool IsOpen)
{
    internal static ExportIntervalDto Create(ExportedInterval interval, TimeZoneInfo zone) => new(
        interval.Kind.ToDto(),
        interval.App is null ? null : AppDto.Create(interval.App),
        interval.Title,
        interval.Start.In(zone),
        interval.End.In(zone),
        interval.Duration.ToMs(),
        interval.IsOpen);
}

/// <summary>One app's active time on one local day.</summary>
/// <param name="Date">The local date.</param>
/// <param name="App">The app.</param>
/// <param name="DurationMs">Active time in the app that day, in milliseconds.</param>
public sealed record ExportDailyAppDto(DateOnly Date, AppDto App, long DurationMs)
{
    internal static ExportDailyAppDto Create(DailyAppTotal total) =>
        new(total.Day, AppDto.Create(total.App), total.Duration.ToMs());
}
