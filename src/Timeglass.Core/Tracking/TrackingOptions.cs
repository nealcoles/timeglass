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

    /// <summary>
    /// After this long without keyboard or mouse input the user counts as away.
    /// <see langword="null"/> turns idle detection off.
    /// </summary>
    public TimeSpan? IdleThreshold { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// App keys (see <see cref="Domain.AppIdentity.Key"/>) that never go idle while in the
    /// foreground, such as video players or meeting apps. Matched case-insensitively.
    /// </summary>
    public IReadOnlySet<string> IdleExemptApps { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Default options.</summary>
    public static TrackingOptions Default { get; } = new();

    /// <summary>Returns a copy with the given idle-exempt app keys.</summary>
    public TrackingOptions WithIdleExemptApps(params IEnumerable<string> appKeys) =>
        this with { IdleExemptApps = new HashSet<string>(appKeys, StringComparer.OrdinalIgnoreCase) };
}
