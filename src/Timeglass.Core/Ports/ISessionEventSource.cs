namespace Timeglass.Core.Ports;

/// <summary>Session and power events that start or end time at the computer.</summary>
public enum SessionEvent
{
    /// <summary>The workstation was locked.</summary>
    Locked,

    /// <summary>The workstation was unlocked.</summary>
    Unlocked,

    /// <summary>The computer is going to sleep or hibernate.</summary>
    Suspending,

    /// <summary>The computer resumed from sleep or hibernate.</summary>
    Resumed,

    /// <summary>The user is logging off or the computer is shutting down.</summary>
    ShuttingDown,
}

/// <summary>
/// Reports lock, sleep and shutdown events. Implemented with SystemEvents in Timeglass.Windows.
/// </summary>
public interface ISessionEventSource
{
    /// <summary>Raised when a session event happens. May be raised on any thread.</summary>
    event Action<SessionEvent>? SessionChanged;
}
