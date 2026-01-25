namespace MirClient.Assets.Caching;

public sealed class LruCache<TKey, TValue> where TKey : notnull
{
    private readonly object _gate = new();
    private readonly Dictionary<TKey, LinkedListNode<Entry>> _map;
    private readonly LinkedList<Entry> _lru = new();
    private readonly Func<TValue, long> _weight;
    private readonly Action<TKey, TValue>? _onEvict;
    private long _currentWeight;
    private long _hits;
    private long _misses;

    public LruCache(long budgetWeight, Func<TValue, long> weight, Action<TKey, TValue>? onEvict = null, IEqualityComparer<TKey>? comparer = null)
    {
        if (budgetWeight < 0)
            throw new ArgumentOutOfRangeException(nameof(budgetWeight), "Budget must be non-negative.");

        BudgetWeight = budgetWeight;
        _weight = weight ?? throw new ArgumentNullException(nameof(weight));
        _onEvict = onEvict;
        _map = new Dictionary<TKey, LinkedListNode<Entry>>(comparer);
    }

    public long BudgetWeight { get; private set; }

    public LruCacheStats Stats
    {
        get
        {
            lock (_gate)
            {
                return new LruCacheStats(
                    Count: _map.Count,
                    BudgetWeight: BudgetWeight,
                    CurrentWeight: _currentWeight,
                    Hits: _hits,
                    Misses: _misses);
            }
        }
    }

    public void SetBudget(long budgetWeight)
    {
        if (budgetWeight < 0)
            throw new ArgumentOutOfRangeException(nameof(budgetWeight), "Budget must be non-negative.");

        lock (_gate)
        {
            BudgetWeight = budgetWeight;
            TrimLocked();
        }
    }

    public bool TryGet(TKey key, out TValue value)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                _hits++;
                value = node.Value.Value;
                TouchLocked(node);
                return true;
            }

            _misses++;
            value = default!;
            return false;
        }
    }

    public bool ContainsKey(TKey key)
    {
        lock (_gate)
        {
            return _map.ContainsKey(key);
        }
    }

    public void AddOrUpdate(TKey key, TValue value, bool trim = true)
    {
        lock (_gate)
        {
            if (_map.TryGetValue(key, out LinkedListNode<Entry>? node))
            {
                TValue oldValue = node.Value.Value;
                long oldWeight = node.Value.Weight;
                node.Value = new Entry(key, value, SafeWeight(value));
                TouchLocked(node);

                _currentWeight = checked(_currentWeight - oldWeight + node.Value.Weight);
                if (!ReferenceEquals(oldValue, value))
                    _onEvict?.Invoke(key, oldValue);

                if (trim)
                    TrimLocked();

                return;
            }

            long weight = SafeWeight(value);
            var newNode = new LinkedListNode<Entry>(new Entry(key, value, weight));
            _lru.AddFirst(newNode);
            _map.Add(key, newNode);
            _currentWeight = checked(_currentWeight + weight);

            if (trim)
                TrimLocked();
        }
    }

    public bool Remove(TKey key)
    {
        lock (_gate)
        {
            if (!_map.TryGetValue(key, out LinkedListNode<Entry>? node))
                return false;

            _map.Remove(key);
            _lru.Remove(node);
            _currentWeight = checked(_currentWeight - node.Value.Weight);
            _onEvict?.Invoke(key, node.Value.Value);
            return true;
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            if (_onEvict != null)
            {
                foreach (Entry entry in _lru)
                {
                    _onEvict(entry.Key, entry.Value);
                }
            }

            _map.Clear();
            _lru.Clear();
            _currentWeight = 0;
        }
    }

    public void TrimToBudget()
    {
        lock (_gate)
        {
            TrimLocked();
        }
    }

    private long SafeWeight(TValue value)
    {
        long weight = _weight(value);
        if (weight < 0)
            throw new InvalidOperationException("Cache entry weight must be non-negative.");

        return weight;
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
        long budget = BudgetWeight;
        if (budget <= 0)
        {
            while (_lru.Last is { } last)
                EvictNodeLocked(last);

            return;
        }

        while (_currentWeight > budget && _lru.Last is { } lastNode)
        {
            EvictNodeLocked(lastNode);
        }
    }

    private void EvictNodeLocked(LinkedListNode<Entry> node)
    {
        _lru.Remove(node);
        _map.Remove(node.Value.Key);
        _currentWeight = checked(_currentWeight - node.Value.Weight);
        _onEvict?.Invoke(node.Value.Key, node.Value.Value);
    }

    private readonly record struct Entry(TKey Key, TValue Value, long Weight);
}
