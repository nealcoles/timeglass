using Timeglass.Core.Domain;
using Timeglass.Windows.Processes;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace Timeglass.Windows.Foreground;

/// <summary>
/// Reads a <see cref="WindowSnapshot"/> for a window handle.
/// </summary>
/// <remarks>
/// The process behind the window is resolved by a <see cref="ProcessResolver"/> (exe path, file
/// description, caching, elevated processes). For UWP apps hosted by <c>ApplicationFrameHost.exe</c>,
/// the snapshot describes the app's own process, found through the frame window's children, while
/// the title stays the frame window's.
/// </remarks>
internal sealed class WindowInspector
{
    /// <summary>Titles longer than this are cut off.</summary>
    public const int MaxTitleLength = 512;

    private const int MaxClassNameLength = 256;

    private readonly ProcessResolver _processes;

    public WindowInspector(ProcessResolver processes)
    {
        _processes = processes;
    }

    /// <summary>An inspector that uses the real Win32 process APIs.</summary>
    public static WindowInspector CreateDefault() =>
        new(new ProcessResolver(Win32ProcessOpener.Instance, Win32ProcessOpener.ReadFileDescription));

    /// <summary>Reads the title of a window, or null if it has none.</summary>
    public static string? ReadTitle(HWND window)
    {
        Span<char> buffer = stackalloc char[MaxTitleLength + 1];
        var length = PInvoke.GetWindowText(window, buffer);
        return length > 0 ? new string(buffer[..length]) : null;
    }

    /// <summary>Reads a snapshot, or returns null for a null handle.</summary>
    public WindowSnapshot? Read(HWND window)
    {
        if (window.IsNull)
        {
            return null;
        }

        var processId = ProcessIdOf(window);
        var process = _processes.Resolve(processId);

        if (UwpAppLocator.IsFrameHost(process.ExeName)
            && UwpAppLocator.PickAppProcess(processId, ChildrenOf(window)) is { } appProcessId)
        {
            var app = _processes.Resolve(appProcessId);
            if (!string.IsNullOrEmpty(app.ExeName))
            {
                processId = appProcessId;
                process = app;
            }
        }

        return new WindowSnapshot(
            ProcessId: (int)processId,
            ExePath: process.ExePath,
            ExeName: process.ExeName,
            Title: ReadTitle(window),
            FileDescription: process.FileDescription);
    }

    private static unsafe uint ProcessIdOf(HWND window)
    {
        uint processId;
        _ = PInvoke.GetWindowThreadProcessId(window, &processId);
        return processId;
    }

    private static List<ChildWindow> ChildrenOf(HWND window)
    {
        var children = new List<ChildWindow>();
        PInvoke.EnumChildWindows(window, (child, _) =>
        {
            children.Add(new ChildWindow(ProcessIdOf(child), ReadClassName(child)));
            return true;
        }, default);
        return children;
    }

    private static string? ReadClassName(HWND window)
    {
        Span<char> buffer = stackalloc char[MaxClassNameLength];
        int length;
        unsafe
        {
            fixed (char* chars = buffer)
            {
                length = PInvoke.GetClassName(window, chars, buffer.Length);
            }
        }

        return length > 0 ? new string(buffer[..length]) : null;
    }
}
