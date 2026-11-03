namespace Timeglass.Windows.Idle;

/// <summary>Tick arithmetic for <c>GetLastInputInfo</c>, kept free of Win32 so it can be unit-tested.</summary>
internal static class IdleTicks
{
    /// <summary>
    /// Milliseconds since the last input.
    /// </summary>
    /// <param name="nowTick">Milliseconds since boot, from <see cref="Environment.TickCount64"/>.</param>
    /// <param name="lastInputTick">
    /// <c>LASTINPUTINFO.dwTime</c>: the tick count at the last input, truncated to 32 bits, so it wraps
    /// to 0 every 2^32 ms (about 49.7 days of uptime).
    /// </param>
    /// <remarks>
    /// Subtracting in 32-bit unsigned arithmetic gives the right answer across a wrap, as long as the
    /// real idle time is shorter than one wrap period. A difference in the upper half of the range
    /// means the input tick is slightly ahead of <paramref name="nowTick"/> (the two were read at
    /// different moments), so it counts as 0 rather than as weeks of idle. The result never exceeds
    /// the uptime.
    /// </remarks>
    public static long MillisecondsSinceLastInput(long nowTick, uint lastInputTick)
    {
        var elapsed = unchecked((uint)nowTick - lastInputTick);
        if (elapsed > int.MaxValue)
        {
            return 0;
        }

        return Math.Min(elapsed, Math.Max(nowTick, 0));
    }
}
