using System.Runtime.CompilerServices;
using Timeglass.Core.Domain;

namespace Timeglass.Core.Reporting;

/// <summary>
/// What an export leaves out, from the user's privacy settings as they are now. Intervals stored
/// before an app was excluded, or before titles were turned off, are still in the database (until
/// the user deletes them); an export does not hand them out again.
/// </summary>
public sealed class ExportPrivacy
{
    private readonly HashSet<string> _excludedApps;
    private readonly HashSet<string> _appsWithoutTitles;

    /// <summary>Creates the rules from the settings' values. App keys are trimmed and compared without case, as the tracker does.</summary>
    /// <param name="includeTitles">False to leave out every window title.</param>
    /// <param name="excludedApps">Keys of apps whose time is left out altogether.</param>
    /// <param name="appsWithoutTitles">Keys of apps whose titles are left out.</param>
    public ExportPrivacy(bool includeTitles = true, IEnumerable<string>? excludedApps = null, IEnumerable<string>? appsWithoutTitles = null)
    {
        IncludeTitles = includeTitles;
        _excludedApps = KeySet(excludedApps);
        _appsWithoutTitles = KeySet(appsWithoutTitles);
    }

    /// <summary>Leaves nothing out.</summary>
    public static ExportPrivacy None { get; } = new();

    /// <summary>Whether any window title is exported.</summary>
    public bool IncludeTitles { get; }

    /// <summary>True if the app's time is left out.</summary>
    public bool Excludes(AppIdentity app)
    {
        ArgumentNullException.ThrowIfNull(app);
        return _excludedApps.Contains(app.Key);
    }

    /// <summary>The title to export for time in <paramref name="app"/>: <paramref name="title"/>, or null if it must be left out.</summary>
    public string? TitleFor(AppIdentity app, string? title)
    {
        ArgumentNullException.ThrowIfNull(app);
        return IncludeTitles && !_appsWithoutTitles.Contains(app.Key) ? title : null;
    }

    private static HashSet<string> KeySet(IEnumerable<string>? keys) =>
        new((keys ?? []).Where(k => k is not null).Select(k => k.Trim()), StringComparer.OrdinalIgnoreCase);
}

/// <summary>One exported stretch of time, clipped to the exported range.</summary>
/// <param name="Kind">Active or away.</param>
/// <param name="Start">Start (inclusive).</param>
/// <param name="End">End (exclusive); for an open interval, the time of the export.</param>
/// <param name="App">The app of active time (<see cref="AppIdentity.Unknown"/> if it wasn't identified); null for away time.</param>
/// <param name="Title">The window title, if one was recorded and may be exported.</param>
/// <param name="IsOpen">True if the interval was still running.</param>
public sealed record ExportedInterval(
    IntervalKind Kind,
    DateTimeOffset Start,
    DateTimeOffset End,
    AppIdentity? App,
    string? Title,
    bool IsOpen)
{
    /// <summary>Length of the exported stretch.</summary>
    public TimeSpan Duration => End - Start;
}

/// <summary>One app's active time on one local day.</summary>
/// <param name="Day">The local date.</param>
/// <param name="App">The app.</param>
/// <param name="Duration">Active time in the app that day.</param>
public sealed record DailyAppTotal(DateOnly Day, AppIdentity App, TimeSpan Duration);

/// <summary>
/// Turns stored intervals into export rows, one at a time, so a range of any length can be
/// written out without holding it in memory.
/// </summary>
public static class Exports
{
    /// <summary>
    /// The instants [start, end) from the start of local day <paramref name="firstDay"/> to the end
    /// of <paramref name="lastDay"/> in <paramref name="zone"/>. Unlike
    /// <see cref="RangeSummaries.Bounds"/>, the range can be any length.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="lastDay"/> is before <paramref name="firstDay"/>.</exception>
    public static (DateTimeOffset Start, DateTimeOffset End) Bounds(DateOnly firstDay, DateOnly lastDay, TimeZoneInfo zone, TimeSpan dayStart = default)
    {
        if (lastDay < firstDay)
        {
            throw new ArgumentException("The range must not end before it starts.", nameof(lastDay));
        }

        return (DaySummaries.Bounds(firstDay, zone, dayStart).Start, DaySummaries.Bounds(lastDay, zone, dayStart).End);
    }

    /// <summary>
    /// The intervals of [<paramref name="from"/>, <paramref name="to"/>) in the order they come,
    /// clipped to the range. An open interval runs until <paramref name="now"/>. Time in apps that
    /// <paramref name="privacy"/> excludes is left out, as are the titles it hides.
    /// </summary>
    /// <param name="intervals">Stored intervals, for example from a range query.</param>
    /// <param name="from">Start of the range (inclusive).</param>
    /// <param name="to">End of the range (exclusive).</param>
    /// <param name="now">The current time, so a still-open interval has an end.</param>
    /// <param name="privacy">What to leave out.</param>
    /// <param name="cancellationToken">Stops the enumeration.</param>
    public static async IAsyncEnumerable<ExportedInterval> Intervals(
        IAsyncEnumerable<ActivityInterval> intervals,
        DateTimeOffset from,
        DateTimeOffset to,
        DateTimeOffset now,
        ExportPrivacy privacy,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(privacy);

        await foreach (var interval in intervals.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (Clip(interval, from, to, now, privacy) is { } exported)
            {
                yield return exported;
            }
        }
    }

    /// <summary>
    /// Active time per local day and app over days <paramref name="firstDay"/> to
    /// <paramref name="lastDay"/> (both included): days in date order, and within a day the app
    /// with the most time first, as in the day summary. Days and apps without time have no entry.
    /// Time crossing a day boundary counts towards each day it covers.
    /// </summary>
    /// <remarks>
    /// A day is handed out as soon as an interval starting on a later day arrives, so only the days
    /// one interval spans are held at a time.
    /// </remarks>
    /// <param name="intervals">Stored intervals <b>ordered by start</b>, as a range query returns them.</param>
    /// <param name="firstDay">First local date.</param>
    /// <param name="lastDay">Last local date, included.</param>
    /// <param name="zone">The user's time zone.</param>
    /// <param name="now">The current time, so a still-open interval has an end.</param>
    /// <param name="privacy">What to leave out.</param>
    /// <param name="dayStart">Local time each day begins; see <see cref="DaySummaries.Bounds"/>.</param>
    /// <param name="cancellationToken">Stops the enumeration.</param>
    public static async IAsyncEnumerable<DailyAppTotal> DailyApps(
        IAsyncEnumerable<ActivityInterval> intervals,
        DateOnly firstDay,
        DateOnly lastDay,
        TimeZoneInfo zone,
        DateTimeOffset now,
        ExportPrivacy privacy,
        TimeSpan dayStart = default,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(privacy);
        var (from, to) = Bounds(firstDay, lastDay, zone, dayStart);

        // The days still being added to, oldest first.
        var pending = new SortedDictionary<DateOnly, Dictionary<string, AppTime>>();

        await foreach (var interval in intervals.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (Clip(interval, from, to, now, privacy) is not { Kind: IntervalKind.Active, App: { } app } exported)
            {
                continue;
            }

            var start = exported.Start;
            var day = DaySummaries.DayOf(start, zone, dayStart);

            // Intervals come ordered by start, so nothing later can add to an earlier day.
            while (pending.Count > 0 && pending.First() is var oldest && oldest.Key < day)
            {
                pending.Remove(oldest.Key);
                foreach (var total in Totals(oldest.Key, oldest.Value))
                {
                    yield return total;
                }
            }

            while (start < exported.End)
            {
                var end = Clipping.Min(exported.End, DaySummaries.Bounds(day, zone, dayStart).End);
                if (!pending.TryGetValue(day, out var apps))
                {
                    pending[day] = apps = new Dictionary<string, AppTime>(StringComparer.Ordinal);
                }

                // Display names can change (a rename in settings); keep the latest one.
                apps[app.Key] = new AppTime(app, apps.GetValueOrDefault(app.Key).Duration + (end - start));
                start = end;
                day = day.AddDays(1);
            }
        }

        foreach (var (day, apps) in pending)
        {
            foreach (var total in Totals(day, apps))
            {
                yield return total;
            }
        }
    }

    /// <summary>
    /// <paramref name="interval"/> as exported from [<paramref name="from"/>, <paramref name="to"/>),
    /// or null if none of it is in the range or its app is excluded.
    /// </summary>
    internal static ExportedInterval? Clip(ActivityInterval interval, DateTimeOffset from, DateTimeOffset to, DateTimeOffset now, ExportPrivacy privacy)
    {
        var start = Clipping.Max(interval.Start, from);
        var end = Clipping.Min(Clipping.EffectiveEnd(interval, now), to);
        if (end <= start)
        {
            return null;
        }

        if (interval.Kind == IntervalKind.Away)
        {
            return new ExportedInterval(IntervalKind.Away, start, end, App: null, Title: null, interval.IsOpen);
        }

        var app = interval.App ?? AppIdentity.Unknown;
        return privacy.Excludes(app)
            ? null
            : new ExportedInterval(IntervalKind.Active, start, end, app, privacy.TitleFor(app, interval.Title), interval.IsOpen);
    }

    private static IEnumerable<DailyAppTotal> Totals(DateOnly day, Dictionary<string, AppTime> apps) => apps.Values
        .OrderByDescending(a => a.Duration)
        .ThenBy(a => a.App.DisplayName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(a => a.App.Key, StringComparer.Ordinal)
        .Select(a => new DailyAppTotal(day, a.App, a.Duration));

    private readonly record struct AppTime(AppIdentity App, TimeSpan Duration);
}
