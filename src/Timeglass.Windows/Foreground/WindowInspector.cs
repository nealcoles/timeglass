using Timeglass.Core.Domain;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace Timeglass.Windows.Foreground;

/// <summary>
/// Reads a <see cref="WindowSnapshot"/> for a window handle.
/// </summary>
/// <remarks>
/// Basic version: process id, exe path and title. UWP apps, elevated processes, file descriptions
/// and caching come with #16.
/// </remarks>
internal static class WindowInspector
{
    /// <summary>Titles longer than this are cut off.</summary>
    public const int MaxTitleLength = 512;

    /// <summary>Reads the title of a window, or null if it has none.</summary>
    public static string? ReadTitle(HWND window)
    {
        Span<char> buffer = stackalloc char[MaxTitleLength + 1];
        var length = PInvoke.GetWindowText(window, buffer);
        return length > 0 ? new string(buffer[..length]) : null;
    }

    /// <summary>Reads a snapshot, or returns null for a null handle.</summary>
    public static WindowSnapshot? Read(HWND window)
    {
        if (window.IsNull)
        {
            return null;
        }

        uint processId;
        unsafe
        {
            _ = PInvoke.GetWindowThreadProcessId(window, &processId);
        }

        var exePath = ReadExePath(processId) ?? string.Empty;
        return new WindowSnapshot(
            ProcessId: (int)processId,
            ExePath: exePath,
            ExeName: Path.GetFileName(exePath),
            Title: ReadTitle(window));
    }

    private static string? ReadExePath(uint processId)
    {
        if (processId == 0)
        {
            return null;
        }

        using var process = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (process.IsInvalid)
        {
            return null;
        }

        Span<char> buffer = stackalloc char[1024];
        var size = (uint)buffer.Length;
        return PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref size)
            ? new string(buffer[..(int)size])
            : null;
    }
}
