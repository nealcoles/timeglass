using Microsoft.Win32;
using Timeglass.Core.Ports;

namespace Timeglass.Windows.Sessions;

/// <summary>Maps <see cref="SystemEvents"/> notifications to the Core <see cref="SessionEvent"/>.</summary>
internal static class SessionEventMapper
{
    /// <summary>The session event for a <see cref="SystemEvents.SessionSwitch"/> reason, or null to ignore it.</summary>
    /// <remarks>
    /// A disconnect (remote desktop closed, or fast user switching to another user) means nobody is at
    /// this session any more, so it counts as a lock; Windows doesn't always send a lock as well. A
    /// connect means the user has signed in to the session again, so it counts as an unlock; a second
    /// unlock right after is harmless (the engine treats it as a foreground change). Logon and logoff
    /// of other sessions and remote control changes don't affect time at this session; logoff of this
    /// session arrives as <see cref="SystemEvents.SessionEnding"/>.
    /// </remarks>
    public static SessionEvent? FromSessionSwitch(SessionSwitchReason reason) => reason switch
    {
        SessionSwitchReason.SessionLock => SessionEvent.Locked,
        SessionSwitchReason.SessionUnlock => SessionEvent.Unlocked,
        SessionSwitchReason.ConsoleDisconnect or SessionSwitchReason.RemoteDisconnect => SessionEvent.Locked,
        SessionSwitchReason.ConsoleConnect or SessionSwitchReason.RemoteConnect => SessionEvent.Unlocked,
        _ => null,
    };

    /// <summary>The session event for a <see cref="SystemEvents.PowerModeChanged"/> mode, or null to ignore it.</summary>
    /// <remarks><see cref="PowerModes.StatusChange"/> (battery or AC changes) is ignored.</remarks>
    public static SessionEvent? FromPowerMode(PowerModes mode) => mode switch
    {
        PowerModes.Suspend => SessionEvent.Suspending,
        PowerModes.Resume => SessionEvent.Resumed,
        _ => null,
    };
}
