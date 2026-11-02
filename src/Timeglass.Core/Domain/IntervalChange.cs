namespace Timeglass.Core.Domain;

/// <summary>What happened to an interval.</summary>
public enum IntervalChangeKind
{
    /// <summary>A new interval started.</summary>
    Opened,

    /// <summary>An open interval moved its end forward (checkpoint) or changed its content.</summary>
    Updated,

    /// <summary>The interval ended. It will not change again.</summary>
    Closed,
}

/// <summary>A change to an interval, emitted by the tracking engine.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="Interval">The full interval after the change.</param>
public sealed record IntervalChange(IntervalChangeKind Kind, ActivityInterval Interval);
