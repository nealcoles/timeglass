namespace Timeglass.Api;

/// <summary>
/// Deletes recorded time, for <c>DELETE /api/intervals</c>. In the tray app this goes through the
/// tracker (<c>TrackingService.DeleteRangeAsync</c>), so the interval being recorded right now is
/// cut too instead of being written back at its next checkpoint.
/// </summary>
public interface IDataDeletion
{
    /// <summary>
    /// Deletes everything recorded in [<paramref name="from"/>, <paramref name="to"/>), cutting
    /// intervals that overlap its edges. Completes once the deletion is stored.
    /// </summary>
    Task DeleteRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
