namespace Timeglass.Core.Tracking;

/// <summary>Tuning knobs for <see cref="TrackingEngine"/>.</summary>
public sealed record TrackingOptions
{
    /// <summary>
    /// A window has to stay in the foreground at least this long to get its own interval.
    /// Shorter visits (Alt-Tab flicker) count towards the previous interval.
    /// </summary>
    public TimeSpan MinimumInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>How often the open interval's end is written out, bounding data loss on a crash.</summary>
    public TimeSpan CheckpointInterval { get; init; } = TimeSpan.FromSeconds(15);

    /// <summary>How often the tracker ticks.</summary>
    public TimeSpan TickInterval { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>Default options.</summary>
    public static TrackingOptions Default { get; } = new();
}
