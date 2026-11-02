using Timeglass.Core.Domain;

namespace Timeglass.Core.Tracking;

/// <summary>An input to <see cref="TrackingEngine"/>. Every event carries the time it happened.</summary>
public abstract record TrackingEvent(DateTimeOffset At);

/// <summary>Tracking starts, with the window that is in the foreground right now.</summary>
public sealed record TrackingStarted(DateTimeOffset At, WindowSnapshot? Window) : TrackingEvent(At);

/// <summary>The foreground window or its title changed.</summary>
public sealed record ForegroundChanged(DateTimeOffset At, WindowSnapshot? Window) : TrackingEvent(At);

/// <summary>
/// Periodic heartbeat. Commits pending window changes and checkpoints the open interval.
/// </summary>
/// <param name="At">Current time.</param>
/// <param name="LastInputAt">Time of the last user input, used for idle detection.</param>
public sealed record Tick(DateTimeOffset At, DateTimeOffset LastInputAt) : TrackingEvent(At);

/// <summary>Tracking stops. Everything is closed.</summary>
public sealed record TrackingStopped(DateTimeOffset At) : TrackingEvent(At);
