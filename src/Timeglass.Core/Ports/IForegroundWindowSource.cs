using Timeglass.Core.Domain;

namespace Timeglass.Core.Ports;

/// <summary>
/// Reports which window is in the foreground. Implemented with Win32 hooks in Timeglass.Windows.
/// </summary>
public interface IForegroundWindowSource
{
    /// <summary>
    /// Raised when the foreground window or its title changes. May be raised on any thread.
    /// The argument is <see langword="null"/> when no window is in the foreground.
    /// </summary>
    event Action<WindowSnapshot?>? ForegroundChanged;

    /// <summary>Reads the current foreground window on demand.</summary>
    WindowSnapshot? GetCurrent();
}
