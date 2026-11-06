using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.OpenApi;
using Timeglass.Core.Ports;
using Timeglass.Core.Reporting;

namespace Timeglass.Api;

/// <summary>
/// The local HTTP API the dashboard reads its data from. A host calls
/// <see cref="AddTimeglassApi"/> on its services and <see cref="MapTimeglassApi"/> on its app,
/// and registers an <see cref="IReportingQueries"/> and an <see cref="ITrackingState"/>.
/// </summary>
/// <remarks>
/// <para>
/// Days are local days in <see cref="TimeProvider.LocalTimeZone"/> of the registered
/// <see cref="TimeProvider"/>, starting at midnight. Dates in routes and queries are ISO dates
/// (<c>yyyy-MM-dd</c>); anything else is answered with a 400 Problem Details response.
/// </para>
/// <para>
/// The API itself does not restrict who may call it. Binding to loopback, Host header checks and
/// the access token are the host's job (#29).
/// </para>
/// </remarks>
public static class TimeglassApi
{
    /// <summary>The OpenAPI document's name; it is served at <c>/openapi/v1.json</c>.</summary>
    public const string DocumentName = "v1";

    /// <summary>Where the OpenAPI document is served.</summary>
    public const string OpenApiPath = "/openapi/" + DocumentName + ".json";

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>
    /// Registers what the endpoints need besides the data: the OpenAPI document, Problem Details
    /// and a <see cref="TimeProvider"/> (the system clock unless one is registered already).
    /// </summary>
    public static IServiceCollection AddTimeglassApi(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        services.AddProblemDetails();

        // The web defaults also accept numbers written as strings, which would make every number
        // in the OpenAPI document "integer or string". The API only writes numbers as numbers.
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.NumberHandling = JsonNumberHandling.Strict);

        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info.Title = "Timeglass";
                document.Info.Version = DocumentName;
                document.Info.Description = "Local API of the Timeglass activity tracker. All durations are in milliseconds.";

                // The document is the same wherever it is served from, so the committed copy stays stable.
                document.Servers = null;
                return Task.CompletedTask;
            });

            // Dates are bound as strings so a bad one gets a Problem Details answer; say they are dates.
            options.AddOperationTransformer((operation, _, _) =>
            {
                foreach (var parameter in operation.Parameters ?? [])
                {
                    if (parameter.Name is "date" or "from" or "to" && parameter.Schema is OpenApiSchema schema)
                    {
                        schema.Format = "date";
                    }
                }

                return Task.CompletedTask;
            });
        });
        return services;
    }

    /// <summary>Maps the endpoints under <c>/api</c> and the OpenAPI document at <see cref="OpenApiPath"/>.</summary>
    public static IEndpointRouteBuilder MapTimeglassApi(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        endpoints.MapOpenApi(OpenApiPath);

        var api = endpoints.MapGroup("/api");

        api.MapGet("/days/{date}/summary", GetDaySummaryAsync)
            .WithName("GetDaySummary")
            .WithTags("Days")
            .WithSummary("Where the time went on one day")
            .WithDescription("Active and away totals, first and last activity, and active time per app.");

        api.MapGet("/days/{date}/timeline", GetTimelineAsync)
            .WithName("GetTimeline")
            .WithTags("Days")
            .WithSummary("What happened when on one day")
            .WithDescription("Active and away blocks in time order. Intervals of one app less than 5 seconds apart are merged.");

        api.MapGet("/days/{date}/apps/{appKey}", GetAppDayAsync)
            .WithName("GetAppDay")
            .WithTags("Days")
            .WithSummary("One app's time on one day, by window title")
            .WithDescription("The 10 titles with the most time, and the rest together as other.");

        api.MapGet("/range/summary", GetRangeSummaryAsync)
            .WithName("GetRangeSummary")
            .WithTags("Ranges")
            .WithSummary("Totals per day and per app over a range of days")
            .WithDescription($"For the week view. A range is at most {RangeSummaries.MaxDays} days.");

        api.MapGet("/status", GetStatusAsync)
            .WithName("GetStatus")
            .WithTags("Status")
            .WithSummary("What the tracker is doing right now");

        return endpoints;
    }

    private static async Task<Results<Ok<DaySummaryDto>, ValidationProblem>> GetDaySummaryAsync(
        [Description("Local date, yyyy-MM-dd.")] string date,
        [FromServices] IReportingQueries reports,
        [FromServices] TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!TryParseDate(date, out var day))
        {
            return InvalidDate(nameof(date), date);
        }

        var summary = await reports.GetDaySummaryAsync(day, time.LocalTimeZone, time.GetUtcNow(), cancellationToken: cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(DaySummaryDto.Create(summary));
    }

    private static async Task<Results<Ok<TimelineDto>, ValidationProblem>> GetTimelineAsync(
        [Description("Local date, yyyy-MM-dd.")] string date,
        [FromServices] IReportingQueries reports,
        [FromServices] TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!TryParseDate(date, out var day))
        {
            return InvalidDate(nameof(date), date);
        }

        var timeline = await reports.GetTimelineAsync(day, time.LocalTimeZone, time.GetUtcNow(), cancellationToken: cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(TimelineDto.Create(timeline));
    }

    private static async Task<Results<Ok<AppDayDto>, ValidationProblem>> GetAppDayAsync(
        [Description("Local date, yyyy-MM-dd.")] string date,
        [Description("The app's key, e.g. outlook.")] string appKey,
        [FromServices] IReportingQueries reports,
        [FromServices] TimeProvider time,
        CancellationToken cancellationToken)
    {
        if (!TryParseDate(date, out var day))
        {
            return InvalidDate(nameof(date), date);
        }

        var breakdown = await reports.GetTitleBreakdownAsync(appKey, day, day, time.LocalTimeZone, time.GetUtcNow(), cancellationToken: cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(AppDayDto.Create(day, breakdown));
    }

    private static async Task<Results<Ok<RangeSummaryDto>, ValidationProblem>> GetRangeSummaryAsync(
        [Description("First local date, yyyy-MM-dd. Required.")] string? from,
        [Description("Last local date, yyyy-MM-dd, included. Required.")] string? to,
        [FromServices] IReportingQueries reports,
        [FromServices] TimeProvider time,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        var hasFrom = TryParseDate(from, out var firstDay);
        var hasTo = TryParseDate(to, out var lastDay);
        if (!hasFrom)
        {
            errors[nameof(from)] = [DateError(from)];
        }

        if (!hasTo)
        {
            errors[nameof(to)] = [DateError(to)];
        }

        if (hasFrom && hasTo)
        {
            if (lastDay < firstDay)
            {
                errors[nameof(to)] = ["The range must not end before it starts."];
            }
            else if (lastDay.DayNumber - firstDay.DayNumber + 1 > RangeSummaries.MaxDays)
            {
                errors[nameof(to)] = [$"A range can be at most {RangeSummaries.MaxDays} days."];
            }
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var summary = await reports.GetRangeSummaryAsync(firstDay, lastDay, time.LocalTimeZone, time.GetUtcNow(), cancellationToken: cancellationToken).ConfigureAwait(false);
        return TypedResults.Ok(RangeSummaryDto.Create(summary));
    }

    private static async Task<Ok<StatusDto>> GetStatusAsync(
        [FromServices] ITrackingState tracking,
        [FromServices] IReportingQueries reports,
        [FromServices] TimeProvider time,
        CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var zone = time.LocalTimeZone;
        var today = DaySummaries.DayOf(now, zone);
        if (tracking.IsPaused)
        {
            return TypedResults.Ok(new StatusDto(TrackingStateKind.Paused, tracking.PausedUntil, null, null, today, now));
        }

        // The open interval is the last block of today's timeline, merged with the ones just
        // before it, so "since" is when the current stretch in that app began.
        var timeline = await reports.GetTimelineAsync(today, zone, now, cancellationToken: cancellationToken).ConfigureAwait(false);
        var status = timeline.Blocks is [.., { IsOpen: true } current]
            ? new StatusDto(
                current.Kind.ToDto() == ActivityKind.Away ? TrackingStateKind.Away : TrackingStateKind.Active,
                null,
                current.App is null ? null : AppDto.Create(current.App),
                current.Start,
                today,
                now)
            : new StatusDto(TrackingStateKind.Untracked, null, null, null, today, now);
        return TypedResults.Ok(status);
    }

    private static bool TryParseDate(string? value, out DateOnly date) =>
        DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date);

    private static string DateError(string? value) => string.IsNullOrEmpty(value)
        ? $"A date ({DateFormat}) is required."
        : $"'{value}' is not a valid date; use {DateFormat}.";

    private static ValidationProblem InvalidDate(string name, string? value) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]>(StringComparer.Ordinal) { [name] = [DateError(value)] });
}
