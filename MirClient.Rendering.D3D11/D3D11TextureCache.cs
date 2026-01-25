namespace MirClient.Rendering.D3D11;

public sealed class D3D11TextureCache<TKey> : IDisposable where TKey : notnull
{
    private readonly object _gate = new();
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _lru = new();
    private long _currentBytes;
    private long _hits;
    private long _misses;
    private int _frameDepth;

    public D3D11TextureCache(long budgetBytes, IEqualityComparer<TKey>? comparer = null)
    {
        if (budgetBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(budgetBytes), "Budget must be non-negative.");

        BudgetBytes = budgetBytes;
        _map = new Dictionary<TKey, LinkedListNode<Entry>>(comparer);
    }

    public long BudgetBytes { get; private set; }

    public D3D11TextureCacheStats Stats
    {
        get
        {
            lock (_gate)
            {
                return new D3D11TextureCacheStats(
                    Count: _map.Count,
                    BudgetBytes: BudgetBytes,
                    CurrentBytes: _currentBytes,
                    Hits: _hits,
                    Misses: _misses);
            }
        }
    }

    public IDisposable BeginFrame()
    {
        lock (_gate)
        {
            _frameDepth++;
        }

        return new FrameScope(this);
    }

    public bool TryGet(TKey key, out D3D11Texture2D texture)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                _hits++;
                texture = node.Value.Texture;
                TouchLocked(node);
                return true;
            }

            _misses++;
            texture = null!;
            return false;
        }
    }

    public D3D11Texture2D GetOrCreate(TKey key, Func<D3D11Texture2D> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        if (TryGet(key, out D3D11Texture2D cached))
            return cached;

        D3D11Texture2D created = factory();
        try
        {
            lock (_gate)
            {
                if (_map.TryGetValue(key, out LinkedListNode<Entry>? existing))
                {
                    TouchLocked(existing);
                    created.Dispose();
                    return existing.Value.Texture;
                }

                AddLocked(key, created, trim: _frameDepth == 0);
                return created;
            }
        }
        catch
        {
            created.Dispose();
            throw;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            foreach (Entry entry in _lru)
            {
                entry.Texture.Dispose();
            }

            _map.Clear();
            _lru.Clear();
            _currentBytes = 0;
        }
    }

    public void Dispose() => Clear();

    public void SetBudget(long budgetBytes)
    {
        if (budgetBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(budgetBytes), "Budget must be non-negative.");

        lock (_gate)
        {
            BudgetBytes = budgetBytes;
            if (_frameDepth == 0)
                TrimLocked();
        }
    }

    private void EndFrame()
    {
        lock (_gate)
        {
            if (_frameDepth <= 0)
                throw new InvalidOperationException("EndFrame without BeginFrame.");

            _frameDepth--;
            if (_frameDepth == 0)
                TrimLocked();
        }
    }

    private void AddLocked(TKey key, D3D11Texture2D texture, bool trim)
    {
        long bytes = EstimateBytes(texture);
        var node = new LinkedListNode<Entry>(new Entry(key, texture, bytes));
        _lru.AddFirst(node);
        _map.Add(key, node);
        _currentBytes = checked(_currentBytes + bytes);

        if (trim)
            TrimLocked();
    }

    private void TouchLocked(LinkedListNode<Entry> node)
    {
        if (node.List != _lru)
            return;

        if (node != _lru.First)
        {
            _lru.Remove(node);
            _lru.AddFirst(node);
        }
    }

    private void TrimLocked()
    {
        long budget = BudgetBytes;
        if (budget <= 0)
        {
            while (_lru.Last is { } last)
                EvictNodeLocked(last);

            return;
        }

        while (_currentBytes > budget && _lru.Last is { } node)
        {
            EvictNodeLocked(node);
        }
    }

    private void EvictNodeLocked(LinkedListNode<Entry> node)
    {
        _lru.Remove(node);
        _map.Remove(node.Value.Key);
        _currentBytes = checked(_currentBytes - node.Value.Bytes);
        node.Value.Texture.Dispose();
    }

    private static long EstimateBytes(D3D11Texture2D texture) => checked((long)texture.Width * texture.Height * 4);

    private readonly struct Entry(TKey key, D3D11Texture2D texture, long bytes)
    {
        public TKey Key { get; } = key;
        public D3D11Texture2D Texture { get; } = texture;
        public long Bytes { get; } = bytes;
    }

    public readonly record struct D3D11TextureCacheStats(int Count, long BudgetBytes, long CurrentBytes, long Hits, long Misses);

    private readonly struct FrameScope(D3D11TextureCache<TKey> cache) : IDisposable
    {
        public void Dispose() => cache.EndFrame();
    }
}
