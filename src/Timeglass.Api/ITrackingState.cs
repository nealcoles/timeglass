namespace Timeglass.Api;

/// <summary>
/// Whether tracking is paused, for <c>GET /api/status</c>. Implemented by the host: the tray app's
/// tracking controller, or a fixed state in the dev host (#28).
/// </summary>
public interface ITrackingState
{
    /// <summary>True while tracking is paused.</summary>
    bool IsPaused { get; }

    /// <summary>When the current pause ends by itself; <see langword="null"/> if it doesn't, or if not paused.</summary>
    DateTimeOffset? PausedUntil { get; }
}
