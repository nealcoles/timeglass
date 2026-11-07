namespace Timeglass.Api;

/// <summary>
/// Whether tracking is paused, for <c>GET /api/status</c>, and pausing and resuming it for
/// <c>POST /api/pause</c> and <c>POST /api/resume</c>. Implemented by the host: the tray app's
/// tracking controller, or a stand-in in the dev host (#28).
/// </summary>
public interface ITrackingState
{
    /// <summary>True while tracking is paused.</summary>
    bool IsPaused { get; }

    /// <summary>When the current pause ends by itself; <see langword="null"/> if it doesn't, or if not paused.</summary>
    DateTimeOffset? PausedUntil { get; }

    /// <summary>
    /// Pauses tracking until <paramref name="until"/>, or until <see cref="ResumeAsync"/> when it is
    /// <see langword="null"/>. Completes once the open interval is stored. Pausing while paused
    /// only changes when the pause ends.
    /// </summary>
    Task PauseAsync(DateTimeOffset? until);

    /// <summary>Resumes tracking, if paused, and cancels any pause end.</summary>
    Task ResumeAsync();
}
