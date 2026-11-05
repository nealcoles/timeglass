namespace Timeglass.Core.Domain;

/// <summary>Cutting a time range out of stored intervals.</summary>
public static class IntervalRange
{
    /// <summary>
    /// What is left of <paramref name="interval"/> once [<paramref name="from"/>, <paramref name="to"/>)
    /// is deleted: nothing if it lies inside the range, the interval itself if it doesn't overlap,
    /// a shortened interval if it overlaps one end, or two pieces if the range is in its middle.
    /// </summary>
    /// <remarks>
    /// The first piece keeps the interval's id; a second piece (the part after the range) gets
    /// <paramref name="newId"/>. Zero-length intervals inside the range are deleted.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="to"/> is before <paramref name="from"/>.</exception>
    public static IReadOnlyList<ActivityInterval> Delete(
        ActivityInterval interval,
        DateTimeOffset from,
        DateTimeOffset to,
        Func<DateTimeOffset, Guid> newId)
    {
        ArgumentNullException.ThrowIfNull(interval);
        ArgumentNullException.ThrowIfNull(newId);
        if (to < from)
        {
            throw new ArgumentException("The range must not end before it starts.", nameof(to));
        }

        var overlaps = interval.Start < to && (interval.End > from || interval.Start >= from);
        if (from == to || !overlaps)
        {
            return [interval];
        }

        var pieces = new List<ActivityInterval>(2);
        if (interval.Start < from)
        {
            pieces.Add(interval with { End = from });
        }

        if (interval.End > to)
        {
            pieces.Add(pieces.Count == 0
                ? interval with { Start = to }
                : interval with { Id = newId(to), Start = to });
        }

        return pieces;
    }
}
