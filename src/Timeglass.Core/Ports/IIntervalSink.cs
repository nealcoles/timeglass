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
}
