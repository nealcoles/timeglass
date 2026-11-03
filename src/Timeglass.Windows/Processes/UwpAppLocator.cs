namespace Timeglass.Windows.Processes;

/// <summary>A child window of a top-level window: its owning process and window class.</summary>
internal readonly record struct ChildWindow(uint ProcessId, string? ClassName);

/// <summary>
/// Finds the real app behind a UWP (packaged) app window.
/// </summary>
/// <remarks>
/// UWP apps such as Calculator or Settings draw inside a frame window owned by
/// <c>ApplicationFrameHost.exe</c>. The app itself owns a child window of class
/// <c>Windows.UI.Core.CoreWindow</c> in its own process. When the app is minimized or suspended
/// that child is detached, and the frame host is all there is; the caller then keeps the frame host.
/// </remarks>
internal static class UwpAppLocator
{
    public const string FrameHostExeName = "ApplicationFrameHost.exe";

    public const string CoreWindowClassName = "Windows.UI.Core.CoreWindow";

    /// <summary>True if this exe is the UWP frame host, whose windows hide the real app.</summary>
    public static bool IsFrameHost(string exeName) =>
        string.Equals(exeName, FrameHostExeName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Picks the app process among the frame window's children: a <c>CoreWindow</c> in another
    /// process if there is one, else any child in another process. Null if all children belong to
    /// the frame host.
    /// </summary>
    public static uint? PickAppProcess(uint frameHostProcessId, IEnumerable<ChildWindow> children)
    {
        uint? other = null;
        foreach (var child in children)
        {
            if (child.ProcessId == 0 || child.ProcessId == frameHostProcessId)
            {
                continue;
            }

            if (string.Equals(child.ClassName, CoreWindowClassName, StringComparison.Ordinal))
            {
                return child.ProcessId;
            }

            other ??= child.ProcessId;
        }

        return other;
    }
}
