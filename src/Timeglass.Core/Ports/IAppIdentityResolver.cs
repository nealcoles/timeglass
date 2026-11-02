using Timeglass.Core.Domain;

namespace Timeglass.Core.Ports;

/// <summary>
/// Maps a foreground window to the application it belongs to.
/// </summary>
public interface IAppIdentityResolver
{
    /// <summary>Resolves the app for a window.</summary>
    AppIdentity Resolve(WindowSnapshot window);
}
