using Timeglass.App.Logging;
using Timeglass.Storage;
using Timeglass.Storage.Settings;

namespace Timeglass.App;

/// <summary>
/// Host settings, bound from the <c>Timeglass</c> configuration section (command line such as
/// <c>--Timeglass:DatabasePath=D:\test.db</c>, <c>DOTNET_Timeglass__DatabasePath</c>, or an
/// optional <c>appsettings.json</c> next to the exe). These are for development and tests; the
/// user's settings are in <see cref="Core.Settings.TimeglassSettings"/>.
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

    /// <summary>The user's settings file. Defaults to <see cref="SettingsFile.DefaultPath"/>.</summary>
    public string? SettingsPath { get; set; }

    /// <summary>Folder for the log files. Defaults to <see cref="LogFolder.DefaultPath"/>.</summary>
    public string? LogDirectory { get; set; }
}
