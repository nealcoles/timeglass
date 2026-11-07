using System.ComponentModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Timeglass.Core.Ports;
using Timeglass.Core.Reporting;
using Timeglass.Core.Tracking;

namespace Timeglass.Api;

// The endpoints that change things (#36): settings, pausing and deleting recorded time.
public static partial class TimeglassApi
{
    private static void MapSettingsEndpoints(RouteGroupBuilder api)
    {
        api.MapGet("/settings", GetSettings)
            .WithName("GetSettings")
            .WithTags("Settings")
            .WithSummary("The settings the dashboard can change");

        api.MapPut("/settings", PutSettingsAsync)
            .WithName("PutSettings")
            .WithTags("Settings")
            .WithSummary("Changes the settings")
            .WithDescription("Validated with the same rules as settings.json; nothing is saved if any value is invalid. Settings the dashboard doesn't edit are kept.");

        api.MapPost("/pause", PauseAsync)
            .WithName("Pause")
            .WithTags("Status")
            .WithSummary("Pauses tracking")
            .WithDescription("Pausing while paused changes when the pause ends.");

        api.MapPost("/resume", ResumeAsync)
            .WithName("Resume")
            .WithTags("Status")
            .WithSummary("Resumes tracking");

        api.MapDelete("/intervals", DeleteIntervalsAsync)
            .WithName("DeleteIntervals")
            .WithTags("Data")
            .WithSummary("Deletes the recorded time of a range of days")
            .WithDescription("Everything recorded on the days from and to (included) is deleted for good. The body must confirm the range as from..to.");
    }

    private static Ok<SettingsDto> GetSettings(
        [FromServices] ISettingsStore settings,
        [FromServices] IAutostart? autostart) =>
        TypedResults.Ok(SettingsDto.Create(settings.Current, autostart?.IsEnabled));

    private static async Task<Results<Ok<SettingsDto>, ValidationProblem>> PutSettingsAsync(
        SettingsDto body,
        [FromServices] ISettingsStore settings,
        [FromServices] IAutostart? autostart,
        CancellationToken cancellationToken)
    {
        var updated = body.ToSettings(settings.Current);
        var errors = SettingsValidation.Validate(updated);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        await settings.SaveAsync(updated, cancellationToken).ConfigureAwait(false);
        if (autostart is not null && body.StartWithWindows is { } start && start != autostart.IsEnabled)
        {
            autostart.SetEnabled(start);
        }

        return TypedResults.Ok(SettingsDto.Create(updated, autostart?.IsEnabled));
    }

    private static async Task<Ok<StatusDto>> PauseAsync(
        PauseRequest body,
        [FromServices] ITrackingState tracking,
        [FromServices] IReportingQueries reports,
        [FromServices] TimeProvider time,
        CancellationToken cancellationToken)
    {
        var length = body.Duration switch
        {
            PauseDuration.ThirtyMinutes => PauseLength.ThirtyMinutes,
            PauseDuration.OneHour => PauseLength.OneHour,
            PauseDuration.UntilTomorrow => PauseLength.UntilTomorrow,
            _ => PauseLength.UntilResumed,
        };
        await tracking.PauseAsync(PauseSchedule.EndOf(length, time.GetUtcNow(), time.LocalTimeZone)).ConfigureAwait(false);
        return await GetStatusAsync(tracking, reports, time, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Ok<StatusDto>> ResumeAsync(
        [FromServices] ITrackingState tracking,
        [FromServices] IReportingQueries reports,
        [FromServices] TimeProvider time,
        CancellationToken cancellationToken)
    {
        await tracking.ResumeAsync().ConfigureAwait(false);
        return await GetStatusAsync(tracking, reports, time, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<Results<NoContent, ValidationProblem>> DeleteIntervalsAsync(
        [Description("First local date, yyyy-MM-dd. Required.")] string? from,
        [Description("Last local date, yyyy-MM-dd, included. Required.")] string? to,
        [FromBody] DeleteIntervalsRequest body,
        [FromServices] IDataDeletion deletion,
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
        else if (lastDay == DateOnly.MaxValue)
        {
            errors[nameof(to)] = ["The range must end before the last date there is."];
        }

        if (hasFrom && hasTo && lastDay < firstDay)
        {
            errors[nameof(to)] = ["The range must not end before it starts."];
        }

        var expected = $"{from}..{to}";
        if (errors.Count == 0 && !string.Equals(body.Confirm, expected, StringComparison.Ordinal))
        {
            errors["confirm"] = [$"Confirm the deletion with \"{expected}\"."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var zone = time.LocalTimeZone;
        await deletion.DeleteRangeAsync(DaySummaries.Bounds(firstDay, zone).Start, DaySummaries.Bounds(lastDay, zone).End, cancellationToken).ConfigureAwait(false);
        return TypedResults.NoContent();
    }
}
