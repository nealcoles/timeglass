using Timeglass.Core.Domain;
using Timeglass.Core.Settings;

namespace Timeglass.Core.Reporting;

/// <summary>Applies ordered keyword groups to recorded time without changing the stored intervals.</summary>
public static class KeywordGroups
{
    /// <summary>Builds a range summary grouped by application name or window title.</summary>
    public static RangeSummary Build(
        IEnumerable<ActivityInterval> intervals, IEnumerable<RolledUpTotal> rolledUp,
        DateOnly firstDay, DateOnly lastDay, TimeZoneInfo zone, DateTimeOffset now,
        IReadOnlyList<KeywordGroupSetting> groups)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(rolledUp);
        ArgumentNullException.ThrowIfNull(groups);

        var summary = RangeSummaries.Build(
            intervals.Select(i => i.Kind == IntervalKind.Active ? i with { App = Group(i.App, i.Title, groups) } : i),
            firstDay, lastDay, zone, now,
            rolledUp: rolledUp.Select(t => t.Kind == IntervalKind.Active ? t with { App = Group(t.App, null, groups) } : t));
        var awayName = "Away";
        if (groups.Any(g => string.Equals(g.Name, awayName, StringComparison.OrdinalIgnoreCase)))
        {
            awayName = "Away (tracked)";
            for (var suffix = 2; groups.Any(g => string.Equals(g.Name, awayName, StringComparison.OrdinalIgnoreCase)); suffix++)
            {
                awayName = $"Away (tracked {suffix})";
            }
        }

        var awayGroup = new AppIdentity("away", awayName);
        return summary with
        {
            Apps = IncludeAway(summary.Apps, summary.Away, awayGroup),
            Days = [.. summary.Days.Select(d => d with { Apps = IncludeAway(d.Apps, d.Away, awayGroup) })],
        };
    }

    private static IReadOnlyList<AppDayTotal> IncludeAway(IReadOnlyList<AppDayTotal> apps, TimeSpan away, AppIdentity awayGroup)
    {
        if (away <= TimeSpan.Zero)
        {
            return apps;
        }

        var totals = apps.Select(a => (a.App, a.Duration))
            .Append((App: awayGroup, Duration: away))
            .OrderByDescending(a => a.Duration)
            .ThenBy(a => a.App.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(a => a.App.Key, StringComparer.Ordinal)
            .ToList();
        var sum = totals.Aggregate(TimeSpan.Zero, (total, a) => total + a.Duration);
        var percents = DaySummaries.RoundToHundred([.. totals.Select(a => a.Duration.Ticks)]);
        return [.. totals.Select((a, i) => new AppDayTotal(a.App, a.Duration, (double)a.Duration.Ticks / sum.Ticks, percents[i]))];
    }

    /// <summary>Summarises the applications contributing to one group over a range.</summary>
    public static RangeSummary BuildForGroup(
        IEnumerable<ActivityInterval> intervals, IEnumerable<RolledUpTotal> rolledUp,
        DateOnly firstDay, DateOnly lastDay, TimeZoneInfo zone, DateTimeOffset now,
        IReadOnlyList<KeywordGroupSetting> groups, string groupName)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(rolledUp);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
        return RangeSummaries.Build(
            intervals.Where(i => i.Kind == IntervalKind.Active
                && string.Equals(Group(i.App, i.Title, groups).DisplayName, groupName, StringComparison.OrdinalIgnoreCase)),
            firstDay, lastDay, zone, now,
            rolledUp: rolledUp.Where(t => t.Kind == IntervalKind.Active
                && string.Equals(Group(t.App, null, groups).DisplayName, groupName, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Summarises one application's titles assigned to a group, including retained untitled time.</summary>
    public static TitleBreakdown BuildAppTitles(
        IEnumerable<ActivityInterval> intervals, IEnumerable<RolledUpTotal> rolledUp,
        DateOnly firstDay, DateOnly lastDay, TimeZoneInfo zone, DateTimeOffset now,
        IReadOnlyList<KeywordGroupSetting> groups, string groupName, string appKey)
    {
        ArgumentNullException.ThrowIfNull(intervals);
        ArgumentNullException.ThrowIfNull(rolledUp);
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupName);
        ArgumentException.ThrowIfNullOrWhiteSpace(appKey);
        var (from, to) = RangeSummaries.Bounds(firstDay, lastDay, zone);
        var filtered = intervals.Where(i => i.Kind == IntervalKind.Active
            && string.Equals(Group(i.App, i.Title, groups).DisplayName, groupName, StringComparison.OrdinalIgnoreCase));
        var retained = rolledUp.Where(t => t.Day >= firstDay && t.Day <= lastDay
            && t.Kind == IntervalKind.Active
            && string.Equals(Group(t.App, null, groups).DisplayName, groupName, StringComparison.OrdinalIgnoreCase));
        return TitleBreakdowns.BuildWithRetained(filtered, appKey, from, to, now, retained);
    }

    /// <summary>Returns the first matching group for an application and title.</summary>
    public static AppIdentity Group(AppIdentity? app, string? title, IReadOnlyList<KeywordGroupSetting> groups)
    {
        var name = (app ?? AppIdentity.Unknown).DisplayName;
        for (var i = 0; i < groups.Count; i++)
        {
            if (groups[i].Keywords.Any(keyword =>
                name.Contains(keyword.Trim(), StringComparison.OrdinalIgnoreCase)
                || (title?.Contains(keyword.Trim(), StringComparison.OrdinalIgnoreCase) ?? false)))
            {
                return new AppIdentity($"group-{i}", groups[i].Name);
            }
        }

        return new AppIdentity("ungrouped", "Ungrouped");
    }
}
