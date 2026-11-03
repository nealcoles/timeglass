namespace Timeglass.Windows.Processes;

/// <summary>
/// A small least-recently-used cache. When full, adding a key evicts the entry that was used
/// longest ago.
/// </summary>
/// <remarks>Not thread-safe; callers lock.</remarks>
internal sealed class LruCache<TKey, TValue>
    where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>> _entries;
    private readonly LinkedList<KeyValuePair<TKey, TValue>> _order = new();

    public LruCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
        _entries = new Dictionary<TKey, LinkedListNode<KeyValuePair<TKey, TValue>>>(capacity, comparer);
    }

    public int Count => _entries.Count;

    /// <summary>Looks up a key and marks it as most recently used.</summary>
    public bool TryGet(TKey key, out TValue value)
    {
        if (_entries.TryGetValue(key, out var node))
        {
            _order.Remove(node);
            _order.AddFirst(node);
            value = node.Value.Value;
            return true;
        }

        value = default!;
        return false;
    }

    /// <summary>Adds or replaces a value, evicting the least recently used entry when full.</summary>
    public void Set(TKey key, TValue value)
    {
        if (_entries.TryGetValue(key, out var existing))
        {
            _order.Remove(existing);
        }
        else if (_entries.Count >= _capacity)
        {
            var oldest = _order.Last!;
            _order.RemoveLast();
            _entries.Remove(oldest.Value.Key);
        }

        _entries[key] = _order.AddFirst(new KeyValuePair<TKey, TValue>(key, value));
    }
}
