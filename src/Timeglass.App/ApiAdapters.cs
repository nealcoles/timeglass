using Timeglass.Api;
using Timeglass.Core.Tracking;
using Timeglass.Windows.Autostart;

namespace Timeglass.App;

/// <summary><i>Start with Windows</i> for the dashboard: the same registry value as the tray menu's.</summary>
internal sealed class RegistryAutostart(AutostartRegistration registration) : IAutostart
{
    public bool IsEnabled => registration.IsEnabled;

    public void SetEnabled(bool enabled) => registration.SetEnabled(enabled);
}

/// <summary>
/// Deletes through the tracker, which closes and cuts the interval being recorded if it falls in
/// the range, so it isn't written back at its next checkpoint.
/// </summary>
internal sealed class TrackerDataDeletion(TrackingService service) : IDataDeletion
{
    public Task DeleteRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken) =>
        service.DeleteRangeAsync(from, to, cancellationToken);
}
