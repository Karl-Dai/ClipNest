namespace ClipNest.Core.Models;

/// <summary>Small LRU cache. Its owner serializes access (the UI thread for image caches).</summary>
public sealed class BoundedCache<TKey, TValue> where TKey : notnull
{
    private readonly int _capacity;
    private readonly Dictionary<TKey, LinkedListNode<(TKey Key, TValue Value)>> _items = new();
    private readonly LinkedList<(TKey Key, TValue Value)> _recent = new();
    public int Count => _items.Count;
    public BoundedCache(int capacity)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
        _capacity = capacity;
    }
    public bool TryGet(TKey key, out TValue? value)
    {
        if (!_items.TryGetValue(key, out var node)) { value = default; return false; }
        _recent.Remove(node);
        _recent.AddFirst(node);
        value = node.Value.Value;
        return true;
    }
    public void Add(TKey key, TValue value)
    {
        if (_items.Remove(key, out var existing)) _recent.Remove(existing);
        var node = _recent.AddFirst((key, value));
        _items[key] = node;
        if (_items.Count <= _capacity) return;
        var oldest = _recent.Last!;
        _items.Remove(oldest.Value.Key);
        _recent.RemoveLast();
    }
    public void Clear() { _items.Clear(); _recent.Clear(); }
}
