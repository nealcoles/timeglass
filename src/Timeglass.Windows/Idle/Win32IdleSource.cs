using System.ComponentModel;
using System.Runtime.InteropServices;
using Timeglass.Core.Ports;
using Windows.Win32;
using Windows.Win32.UI.Input.KeyboardAndMouse;

namespace Timeglass.Windows.Idle;

/// <summary>Reports the last keyboard or mouse input of the session using <c>GetLastInputInfo</c>.</summary>
/// <remarks>
/// <c>GetLastInputInfo</c> gives the 32-bit tick count of the last input. It is compared with
/// <see cref="Environment.TickCount64"/> (same clock) to get the idle time, which is then subtracted
/// from the current time of the <see cref="TimeProvider"/>.
/// </remarks>
public sealed class Win32IdleSource(TimeProvider? time = null) : IIdleSource
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <inheritdoc />
    /// <exception cref="Win32Exception"><c>GetLastInputInfo</c> failed.</exception>
    public DateTimeOffset GetLastInputTime()
    {
        var idle = TimeSpan.FromMilliseconds(GetIdleMilliseconds());
        return _time.GetUtcNow() - idle;
    }

    /// <summary>Milliseconds since the last input in this session.</summary>
    internal static long GetIdleMilliseconds()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!PInvoke.GetLastInputInfo(ref info))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError());
        }

        // Read the clock after the input time so it is never behind it.
        return IdleTicks.MillisecondsSinceLastInput(Environment.TickCount64, info.dwTime);
    }
}
