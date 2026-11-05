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
    /// A gap between two events longer than this is a clock jump (sleep without a suspend event,
    /// or a manual clock change) and counts as away. Should be much larger than <see cref="TickInterval"/>.
    /// <see langword="null"/> turns the check off.
    /// </summary>
    public TimeSpan? ClockJumpThreshold { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// App keys (see <see cref="Domain.AppIdentity.Key"/>) that never go idle while in the
    /// foreground, such as video players or meeting apps. Matched case-insensitively.
    /// </summary>
    public IReadOnlySet<string> IdleExemptApps { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Whether window titles are stored. When false, every interval is stored without a title.
    /// </summary>
    public bool RecordWindowTitles { get; init; } = true;

    /// <summary>
    /// App keys that are not tracked at all. While one of them is in front, no interval is open:
    /// the time is a gap, like a pause. Matched case-insensitively.
    /// </summary>
    public IReadOnlySet<string> ExcludedApps { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// App keys whose time is tracked but whose window titles are never stored.
    /// Matched case-insensitively.
    /// </summary>
    public IReadOnlySet<string> AppsWithoutTitles { get; init; } =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Default options.</summary>
    public static TrackingOptions Default { get; } = new();

    /// <summary>Returns a copy with the given idle-exempt app keys.</summary>
    public TrackingOptions WithIdleExemptApps(params IEnumerable<string> appKeys) =>
        this with { IdleExemptApps = KeySet(appKeys) };

    /// <summary>Returns a copy with the given excluded (not tracked) app keys.</summary>
    public TrackingOptions WithExcludedApps(params IEnumerable<string> appKeys) =>
        this with { ExcludedApps = KeySet(appKeys) };

    /// <summary>Returns a copy with the given app keys whose titles are not stored.</summary>
    public TrackingOptions WithAppsWithoutTitles(params IEnumerable<string> appKeys) =>
        this with { AppsWithoutTitles = KeySet(appKeys) };

    private static HashSet<string> KeySet(IEnumerable<string> appKeys) =>
        new(appKeys, StringComparer.OrdinalIgnoreCase);
}
