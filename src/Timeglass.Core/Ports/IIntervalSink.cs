using Timeglass.Core.Domain;

namespace Timeglass.Core.Ports;

/// <summary>
/// Receives interval changes from the tracker. Implemented by storage.
/// Each change carries the full interval; apply it as an upsert by <see cref="ActivityInterval.Id"/>.
/// </summary>
public interface IIntervalSink
{
    /// <summary>Applies a batch of changes in order.</summary>
    ValueTask ApplyAsync(IReadOnlyList<IntervalChange> changes, CancellationToken cancellationToken);
}
