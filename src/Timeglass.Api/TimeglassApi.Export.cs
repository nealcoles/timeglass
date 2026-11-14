using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Timeglass.Core.Domain;
using Timeglass.Core.Ports;
using Timeglass.Core.Reporting;

namespace Timeglass.Api;

// Getting the data out (#39), for spreadsheets, scripts and timesheets.
public static partial class TimeglassApi
{
    private const string ExportOperation = "Export";
    private const string JsonFormat = "json";
    private const string CsvFormat = "csv";
    private const string IntervalsGranularity = "intervals";
    private const string DailyAppsGranularity = "daily-apps";

    // Dates the day arithmetic can't overflow on; nothing was recorded outside them anyway.
    private static readonly DateOnly FirstExportDate = new(1970, 1, 1);
    private static readonly DateOnly LastExportDate = new(9998, 12, 31);

    private static void MapExportEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/export", Export)
            .WithName(ExportOperation)
            .WithTags("Data")
            .WithSummary("The recorded time of a range of days, as a JSON or CSV file")
            .WithDescription(
                "One row per interval, or per day and app (daily-apps). The answer is written as it is read, so a range can be any length. "
                + "Time in apps that are excluded now is left out, and so are the titles the privacy settings hide now. "
                + "In CSV, text that a spreadsheet would run as a formula gets a leading apostrophe.");
    }

    private static Results<Ok<ExportDto>, CsvExport, ValidationProblem> Export(
        [Description("First local date, yyyy-MM-dd. Required.")] string? from,
        [Description("Last local date, yyyy-MM-dd, included. Required.")] string? to,
        [Description("json (the default) or csv.")] string? format,
        [Description("intervals (the default) or daily-apps.")] string? granularity,
        HttpContext context,
        [FromServices] IReportingQueries reports,
        [FromServices] ISettingsStore settings,
        [FromServices] TimeProvider time)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var hasFrom = TryParseDate(from, out var firstDay);
        var hasTo = TryParseDate(to, out var lastDay);
        if (!hasFrom)
        {
            errors[nameof(from)] = [DateError(from)];
        }
        else if (firstDay < FirstExportDate || firstDay > LastExportDate)
        {
            errors[nameof(from)] = [ExportDateError()];
        }

        if (!hasTo)
        {
            errors[nameof(to)] = [DateError(to)];
        }
        else if (lastDay < FirstExportDate || lastDay > LastExportDate)
        {
            errors[nameof(to)] = [ExportDateError()];
        }
        else if (hasFrom && lastDay < firstDay)
        {
            errors[nameof(to)] = ["The range must not end before it starts."];
        }

        format = string.IsNullOrEmpty(format) ? JsonFormat : format;
        if (format is not (JsonFormat or CsvFormat))
        {
            errors[nameof(format)] = [$"'{format}' is not a format; use {JsonFormat} or {CsvFormat}."];
        }

        granularity = string.IsNullOrEmpty(granularity) ? IntervalsGranularity : granularity;
        if (granularity is not (IntervalsGranularity or DailyAppsGranularity))
        {
            errors[nameof(granularity)] = [$"'{granularity}' is not a granularity; use {IntervalsGranularity} or {DailyAppsGranularity}."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var zone = time.LocalTimeZone;
        var now = time.GetUtcNow();
        var current = settings.Current;
        var privacy = new ExportPrivacy(current.RecordWindowTitles, current.ExcludedApps, current.AppsWithoutTitles);
        var (start, end) = Exports.Bounds(firstDay, lastDay, zone);
        var stored = reports.StreamRangeAsync(start, end, context.RequestAborted);

        var byInterval = granularity == IntervalsGranularity;
        var intervals = byInterval ? Exports.Intervals(stored, start, end, now, privacy, context.RequestAborted) : null;
        var dailyApps = byInterval ? null : Exports.DailyApps(stored, firstDay, lastDay, zone, now, privacy, cancellationToken: context.RequestAborted);

        // A file to save, also when the link is opened in a tab; and not one for a cache to keep.
        var fileName = string.Create(CultureInfo.InvariantCulture, $"timeglass-{granularity}-{firstDay:yyyy-MM-dd}_{lastDay:yyyy-MM-dd}.{format}");
        context.Response.Headers.ContentDisposition = $"attachment; filename=\"{fileName}\"";
        context.Response.Headers.CacheControl = "no-store";

        if (format == CsvFormat)
        {
            return intervals is not null
                ? new CsvExport(IntervalCsvHeader, IntervalCsvLines(intervals, zone))
                : new CsvExport(DailyAppCsvHeader, DailyAppCsvLines(dailyApps!));
        }

        return TypedResults.Ok(new ExportDto(
            firstDay,
            lastDay,
            byInterval ? ExportGranularity.Intervals : ExportGranularity.DailyApps,
            intervals is null ? null : Select(intervals, i => ExportIntervalDto.Create(i, zone)),
            dailyApps is null ? null : Select(dailyApps, ExportDailyAppDto.Create)));
    }

    private static string ExportDateError() => string.Create(
        CultureInfo.InvariantCulture,
        $"The date must be between {FirstExportDate:yyyy-MM-dd} and {LastExportDate:yyyy-MM-dd}.");

    // Times carry the zone's offset, as in JSON, so they stay unambiguous when the clocks go back.
    // The date is the local day the interval starts on, for grouping in a spreadsheet.
    private static readonly string IntervalCsvHeader =
        Csv.Line("date", "start", "end", "duration_ms", "kind", "app_key", "app", "title");

    private static readonly string DailyAppCsvHeader = Csv.Line("date", "app_key", "app", "duration_ms");

    private static async IAsyncEnumerable<string> IntervalCsvLines(IAsyncEnumerable<ExportedInterval> intervals, TimeZoneInfo zone)
    {
        var line = new StringBuilder();
        await foreach (var interval in intervals.ConfigureAwait(false))
        {
            var start = interval.Start.In(zone);
            yield return Csv.AppendLine(
                line.Clear(),
                start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                CsvTime(start),
                CsvTime(interval.End.In(zone)),
                interval.Duration.ToMs().ToString(CultureInfo.InvariantCulture),
                interval.Kind == IntervalKind.Away ? "away" : "active",
                interval.App?.Key,
                interval.App?.DisplayName,
                interval.Title).ToString();
        }
    }

    private static async IAsyncEnumerable<string> DailyAppCsvLines(IAsyncEnumerable<DailyAppTotal> totals)
    {
        var line = new StringBuilder();
        await foreach (var total in totals.ConfigureAwait(false))
        {
            yield return Csv.AppendLine(
                line.Clear(),
                total.Day.ToString(DateFormat, CultureInfo.InvariantCulture),
                total.App.Key,
                total.App.DisplayName,
                total.Duration.ToMs().ToString(CultureInfo.InvariantCulture)).ToString();
        }
    }

    private static string CsvTime(DateTimeOffset time) => time.ToString("yyyy-MM-dd'T'HH:mm:ss.fffzzz", CultureInfo.InvariantCulture);

    private static async IAsyncEnumerable<TResult> Select<T, TResult>(
        IAsyncEnumerable<T> source,
        Func<T, TResult> selector,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var item in source.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            yield return selector(item);
        }
    }

    /// <summary>A CSV file written line by line as its rows are read: UTF-8 with a byte order mark, which Excel needs to read it as UTF-8.</summary>
    private sealed class CsvExport(string header, IAsyncEnumerable<string> lines) : IResult
    {
        public async Task ExecuteAsync(HttpContext httpContext)
        {
            var cancellationToken = httpContext.RequestAborted;
            httpContext.Response.ContentType = Csv.ContentType;

            var writer = new StreamWriter(httpContext.Response.Body, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), bufferSize: 16 * 1024, leaveOpen: true);
            await using (writer.ConfigureAwait(false))
            {
                await writer.WriteAsync(header.AsMemory(), cancellationToken).ConfigureAwait(false);
                await foreach (var line in lines.WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    await writer.WriteAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                }

                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
