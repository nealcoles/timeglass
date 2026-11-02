namespace Timeglass.Windows.Foreground;

/// <summary>
/// Decides whether a foreground observation is new. Hooks, title-change events and the polling
/// fallback all report what they see; only real changes (another window, or a new title) pass.
/// </summary>
/// <remarks>Pure logic, no Win32, so it can be tested without a desktop. Not thread-safe.</remarks>
internal sealed class ForegroundChangeDetector
{
    private bool _hasLast;
    private nint _lastWindow;
    private string? _lastTitle;

    /// <summary>
    /// Records an observation and returns true if it differs from the last one that was reported.
    /// </summary>
    /// <param name="window">Foreground window handle; 0 when there is none.</param>
    /// <param name="title">Its title, or null when there is no window or no title.</param>
    public bool IsChange(nint window, string? title)
    {
        if (_hasLast && window == _lastWindow && string.Equals(title, _lastTitle, StringComparison.Ordinal))
        {
            return false;
        }

        _hasLast = true;
        _lastWindow = window;
        _lastTitle = title;
        return true;
    }

    /// <summary>Forgets the last observation, so the next one is always reported.</summary>
    public void Reset() => _hasLast = false;
}
