using Timeglass.Core.Domain;

namespace Timeglass.Core.Ports;

/// <summary>
/// Receives interval changes from the tracker. Implemented by storage.
/// Each change carries the full interval; apply it as an upsert by <see cref="ActivityInterval.Id"/>.
/// </summary>
public interface IIntervalSink
{
    /// <summary>
    /// Applies a batch of changes in order. May return before the changes are durable;
    /// use <see cref="FlushAsync"/> to wait for that.
    /// </summary>
    ValueTask ApplyAsync(IReadOnlyList<IntervalChange> changes, CancellationToken cancellationToken);

    /// <summary>Completes once every change applied so far has been stored.</summary>
    ValueTask FlushAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Deletes everything recorded in [<paramref name="from"/>, <paramref name="to"/>), after the
    /// changes applied so far. Intervals that overlap the range are cut at its edges (see
    /// <see cref="IntervalRange.Delete"/>). The open interval is left alone: the tracker would
    /// write it again at its next checkpoint, so <see cref="Tracking.TrackingService.DeleteRangeAsync"/>
    /// closes it first. Completes once the deletion is stored.
    /// </summary>
    ValueTask DeleteRangeAsync(DateTimeOffset from, DateTimeOffset to, CancellationToken cancellationToken);
}
