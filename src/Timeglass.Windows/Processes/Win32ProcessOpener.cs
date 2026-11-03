using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace Timeglass.Windows.Processes;

/// <summary>Opens processes with <c>PROCESS_QUERY_LIMITED_INFORMATION</c>, the least access that reads the exe path.</summary>
internal sealed class Win32ProcessOpener : IProcessOpener
{
    public static Win32ProcessOpener Instance { get; } = new();

    public ProcessOpenResult Open(uint processId)
    {
        var handle = PInvoke.OpenProcess_SafeHandle(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, processId);
        if (handle.IsInvalid)
        {
            var error = (WIN32_ERROR)Marshal.GetLastPInvokeError();
            handle.Dispose();
            return error == WIN32_ERROR.ERROR_ACCESS_DENIED ? ProcessOpenResult.AccessDenied : ProcessOpenResult.NotFound;
        }

        return ProcessOpenResult.Opened(new OpenProcess(handle));
    }

    /// <summary>Reads the file description from the exe's version info, or null if it has none.</summary>
    public static string? ReadFileDescription(string exePath)
    {
        try
        {
            return FileVersionInfo.GetVersionInfo(exePath).FileDescription;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private sealed class OpenProcess(SafeFileHandle handle) : IOpenProcess
    {
        public long StartTime => PInvoke.GetProcessTimes(handle, out var created, out _, out _, out _)
            ? ((long)created.dwHighDateTime << 32) | (uint)created.dwLowDateTime
            : 0;

        public string? ReadImagePath()
        {
            Span<char> buffer = stackalloc char[1024];
            var size = (uint)buffer.Length;
            return PInvoke.QueryFullProcessImageName(handle, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, buffer, ref size)
                ? new string(buffer[..(int)size])
                : null;
        }

        public void Dispose() => handle.Dispose();
    }
}
