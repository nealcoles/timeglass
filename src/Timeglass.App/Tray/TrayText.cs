using System.Globalization;

namespace Timeglass.App.Tray;

/// <summary>Texts shown by the tray icon.</summary>
internal static class TrayText
{
    /// <summary>Windows cuts notification icon tooltips at 127 characters.</summary>
    public const int MaxTooltipLength = 127;

    /// <summary>Tooltip such as <c>Timeglass: 3 h 05 min active today</c>.</summary>
    public static string Tooltip(TimeSpan? activeToday, bool paused)
    {
        var text = activeToday is { } active
            ? $"Timeglass: {Duration(active)} active today"
            : "Timeglass";

        return paused ? text + " (paused)" : text;
    }

    /// <summary><c>0 min</c>, <c>45 min</c>, <c>3 h 05 min</c>. Whole minutes, rounded down.</summary>
    public static string Duration(TimeSpan duration)
    {
        var minutes = (long)Math.Max(0, Math.Floor(duration.TotalMinutes));
        return minutes < 60
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes} min")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes / 60} h {minutes % 60:00} min");
    }
}
