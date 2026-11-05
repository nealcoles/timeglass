using System.Globalization;

namespace Timeglass.App.Tray;

/// <summary>Texts shown by the tray icon.</summary>
internal static class TrayText
{
    /// <summary>Windows cuts notification icon tooltips at 127 characters.</summary>
    public const int MaxTooltipLength = 127;

    /// <summary>
    /// Tooltip such as <c>Timeglass: 3 h 05 min active today</c>, with <c>(paused)</c> or
    /// <c>(paused until 14:30)</c> after it.
    /// </summary>
    /// <param name="activeToday">Today's active time, if known.</param>
    /// <param name="paused">Whether tracking is paused.</param>
    /// <param name="pausedUntil">Local time the pause ends, if it does.</param>
    public static string Tooltip(TimeSpan? activeToday, bool paused, DateTime? pausedUntil = null)
    {
        var text = activeToday is { } active
            ? $"Timeglass: {Duration(active)} active today"
            : "Timeglass";

        if (!paused)
        {
            return text;
        }

        return pausedUntil is { } until
            ? $"{text} (paused until {Clock(until)})"
            : text + " (paused)";
    }

    /// <summary>The menu item shown while paused: <c>Resume tracking</c>, or <c>Resume tracking (paused until 14:30)</c>.</summary>
    public static string ResumeItem(DateTime? pausedUntil) =>
        pausedUntil is { } until ? $"&Resume tracking (paused until {Clock(until)})" : "&Resume tracking";

    /// <summary><c>14:30</c>; midnight is <c>midnight</c>, since <c>00:00</c> reads as "now" to some.</summary>
    public static string Clock(DateTime local) =>
        local.TimeOfDay == TimeSpan.Zero
            ? "midnight"
            : local.ToString("HH:mm", CultureInfo.InvariantCulture);

    /// <summary><c>0 min</c>, <c>45 min</c>, <c>3 h 05 min</c>. Whole minutes, rounded down.</summary>
    public static string Duration(TimeSpan duration)
    {
        var minutes = (long)Math.Max(0, Math.Floor(duration.TotalMinutes));
        return minutes < 60
            ? string.Create(CultureInfo.InvariantCulture, $"{minutes} min")
            : string.Create(CultureInfo.InvariantCulture, $"{minutes / 60} h {minutes % 60:00} min");
    }
}
