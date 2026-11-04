using Microsoft.Extensions.Logging;
using Timeglass.App.Logging;
using Timeglass.Storage;

namespace Timeglass.App;

/// <summary>
/// Host settings, bound from the <c>Timeglass</c> configuration section (command line such as
/// <c>--Timeglass:DatabasePath=D:\test.db</c>, <c>DOTNET_Timeglass__DatabasePath</c>, or an
/// optional <c>appsettings.json</c> next to the exe). User settings get their own file in #22.
/// </summary>
internal sealed class TimeglassOptions
{
    public const string Section = "Timeglass";

    /// <summary>Database file. Defaults to <see cref="TimeglassDatabase.DefaultPath"/>.</summary>
    public string? DatabasePath { get; set; }

    /// <summary>
    /// Where <i>Open dashboard</i> goes. Empty until the app serves the UI itself (#27, #30);
    /// set it to a dev server URL to try the menu item before then.
    /// </summary>
    public Uri? DashboardUrl { get; set; }

    /// <summary>How often the tray tooltip refreshes today's active time.</summary>
    public TimeSpan TooltipRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Lowest level written to the log file. Changes apply without a restart when the
    /// configuration reloads. <c>Debug</c> adds tracking state changes (never window titles).
    /// </summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Information;

    /// <summary>Folder for the log files. Defaults to <see cref="LogFolder.DefaultPath"/>.</summary>
    public string? LogDirectory { get; set; }
}
