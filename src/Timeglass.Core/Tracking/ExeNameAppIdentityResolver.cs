using Timeglass.Core.Domain;
using Timeglass.Core.Ports;

namespace Timeglass.Core.Tracking;

/// <summary>
/// Minimal resolver: one app per executable, named after the file description or exe name.
/// Replaced by the alias-table resolver in #12.
/// </summary>
public sealed class ExeNameAppIdentityResolver : IAppIdentityResolver
{
    /// <inheritdoc />
    public AppIdentity Resolve(WindowSnapshot window)
    {
        ArgumentNullException.ThrowIfNull(window);

        var name = Path.GetFileNameWithoutExtension(window.ExeName);
        if (string.IsNullOrWhiteSpace(name))
        {
            return AppIdentity.Unknown;
        }

        var display = string.IsNullOrWhiteSpace(window.FileDescription) ? name : window.FileDescription;
        return new AppIdentity(name.ToLowerInvariant(), display);
    }
}
