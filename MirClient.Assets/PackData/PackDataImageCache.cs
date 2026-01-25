using MirClient.Assets.Caching;

namespace MirClient.Assets.PackData;

public sealed class PackDataImageCache : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<PackDataImageKey, Task<PackDataImage?>> _inflight;
    private readonly Dictionary<string, PackDataArchive> _archives;
    private readonly LruCache<PackDataImageKey, PackDataImage> _images;
    private readonly SemaphoreSlim _decodeLimiter;
    private bool _disposed;

    public PackDataImageCache(PackDataImageCacheOptions? options = null)
    {
        options ??= new PackDataImageCacheOptions();

        int maxConcurrentDecodes = options.MaxConcurrentDecodes;
        if (maxConcurrentDecodes <= 0)
            maxConcurrentDecodes = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        _decodeLimiter = new SemaphoreSlim(maxConcurrentDecodes, maxConcurrentDecodes);

        _inflight = new Dictionary<PackDataImageKey, Task<PackDataImage?>>(PackDataImageKeyComparer.Instance);
        _archives = new Dictionary<string, PackDataArchive>(StringComparer.OrdinalIgnoreCase);
        _images = new LruCache<PackDataImageKey, PackDataImage>(
            budgetWeight: options.ImageCacheBytes,
            weight: static img => img.Bgra32.LongLength,
            comparer: PackDataImageKeyComparer.Instance);
    }

    public LruCacheStats ImageCacheStats => _images.Stats;

    public Task<PackDataImage?> GetImageAsync(string dataPath, int imageIndex)
    {
        if (string.IsNullOrWhiteSpace(dataPath))
            throw new ArgumentException("Data path is required.", nameof(dataPath));

        if (_disposed)
            throw new ObjectDisposedException(nameof(PackDataImageCache));

        return GetImageAsyncFullPath(Path.GetFullPath(dataPath), imageIndex);
    }

    public bool TryGetImage(string dataPath, int imageIndex, out PackDataImage image)
    {
        if (string.IsNullOrWhiteSpace(dataPath))
            throw new ArgumentException("Data path is required.", nameof(dataPath));

        if (_disposed)
            throw new ObjectDisposedException(nameof(PackDataImageCache));

        return TryGetImage(new PackDataImageKey(Path.GetFullPath(dataPath), imageIndex), out image);
    }

    public bool TryGetImage(PackDataImageKey key, out PackDataImage image)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(PackDataImageCache));

        return _images.TryGet(key, out image);
    }

    public Task<PackDataImage?> GetImageAsyncFullPath(string fullPath, int imageIndex)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
            throw new ArgumentException("Data path is required.", nameof(fullPath));

        if (_disposed)
            throw new ObjectDisposedException(nameof(PackDataImageCache));

        var key = new PackDataImageKey(fullPath, imageIndex);
        if (_images.TryGet(key, out PackDataImage cached))
            return Task.FromResult<PackDataImage?>(cached);

        lock (_gate)
        {
            if (_images.TryGet(key, out cached))
                return Task.FromResult<PackDataImage?>(cached);

            if (_inflight.TryGetValue(key, out Task<PackDataImage?>? inflight))
                return inflight;

            Task<PackDataImage?> task = Task.Run(async () =>
            {
                await _decodeLimiter.WaitAsync().ConfigureAwait(false);
                try
                {
                    return DecodeAndCache(fullPath, imageIndex);
                }
                finally
                {
                    _decodeLimiter.Release();
                }
            }, CancellationToken.None);
            _inflight.Add(key, task);
            return task;
        }
    }

    public async Task PrefetchAsync(string dataPath, IEnumerable<int> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);

        List<Task> tasks = new();
        foreach (int idx in indices)
        {
            tasks.Add(GetImageAsync(dataPath, idx));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public void ClearImages() => _images.Clear();

    public void Dispose()
    {
        Task<PackDataImage?>[] pending;
        lock (_gate)
        {
            if (_disposed)
                return;

            _disposed = true;
            pending = _inflight.Values.ToArray();
            _inflight.Clear();
        }

        try
        {
            if (pending.Length > 0)
                Task.WaitAll(pending);
        }
        catch
        {
            
        }

        _images.Clear();

        lock (_gate)
        {
            foreach (PackDataArchive archive in _archives.Values)
                archive.Dispose();

            _archives.Clear();
        }
    }

    private PackDataImage? DecodeAndCache(string fullPath, int imageIndex)
    {
        var key = new PackDataImageKey(fullPath, imageIndex);
        try
        {
            if (_disposed)
                return null;

            PackDataArchive archive = GetOrOpenArchive(fullPath);
            if (!archive.TryDecodeImage(imageIndex, out PackDataImage image))
                return null;

            if (!_disposed)
                _images.AddOrUpdate(key, image);

            return image;
        }
        finally
        {
            lock (_gate)
            {
                _inflight.Remove(key);
            }
        }
    }

    private PackDataArchive GetOrOpenArchive(string fullPath)
    {
        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(PackDataImageCache));

            if (_archives.TryGetValue(fullPath, out PackDataArchive? archive))
                return archive;

            archive = new PackDataArchive(fullPath);
            _archives.Add(fullPath, archive);
            return archive;
        }
    }

    private sealed class PackDataArchive : IDisposable
    {
        private readonly object _decodeGate = new();
        private readonly PackDataFile _file;

        public PackDataArchive(string dataPath)
        {
            _file = PackDataFile.Open(dataPath);
        }

        public bool TryDecodeImage(int index, out PackDataImage image)
        {
            lock (_decodeGate)
            {
                return _file.TryDecodeImage(index, out image);
            }
        }

        public void Dispose() => _file.Dispose();
    }
}
