using Timeglass.Core.Settings;

namespace Timeglass.Api;

/// <summary>
/// The user's settings, for <c>GET</c> and <c>PUT /api/settings</c>. In the tray app this is
/// <c>settings.json</c>, which the app watches and applies without a restart (#22).
/// </summary>
public interface ISettingsStore
{
    /// <summary>The settings in effect. Do not modify the instance.</summary>
    TimeglassSettings Current { get; }

    /// <summary>
    /// Stores <paramref name="settings"/>, which the API has already validated, and completes once
    /// they are in effect (or, if applying them takes unusually long, once they are stored).
    /// </summary>
    Task SaveAsync(TimeglassSettings settings, CancellationToken cancellationToken);
}

/// <summary>
/// Starting the app when the user signs in. Optional: a host that can't (the dev host on another
/// OS, say) doesn't register one, and the settings say so with <c>startWithWindows: null</c>.
/// </summary>
public interface IAutostart
{
    /// <summary>Whether signing in starts the app.</summary>
    bool IsEnabled { get; }

    /// <summary>Turns starting with the user's sign-in on or off.</summary>
    void SetEnabled(bool enabled);
}
