namespace Timeglass.Core.Domain;

/// <summary>What a stretch of time was spent on.</summary>
public enum IntervalKind
{
    /// <summary>The user was working in <see cref="ActivityInterval.App"/>.</summary>
    Active = 0,

    /// <summary>The user was away: idle, locked or asleep.</summary>
    Away = 1,
}

/// <summary>
/// A contiguous stretch of time. Intervals produced by the engine never overlap.
/// While <see cref="IsOpen"/> is true, <see cref="End"/> is the last checkpoint and will move forward.
/// </summary>
/// <param name="Id">Unique id, stable across updates of the same interval.</param>
/// <param name="Kind">Active or away.</param>
/// <param name="Start">Start time (inclusive).</param>
/// <param name="End">End time (exclusive), or the latest checkpoint while open.</param>
/// <param name="IsOpen">True while the interval is still running.</param>
/// <param name="App">The application for active intervals; <see langword="null"/> for away intervals.</param>
/// <param name="ExePath">Executable path for active intervals.</param>
/// <param name="Title">Window title, when recorded.</param>
public sealed record ActivityInterval(
    Guid Id,
    IntervalKind Kind,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsOpen,
    AppIdentity? App,
    string? ExePath,
    string? Title)
{
    /// <summary>Length of the interval up to <see cref="End"/>.</summary>
    public TimeSpan Duration => End - Start;
}
