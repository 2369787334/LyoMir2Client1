using MirClient.Assets.Caching;
using MirClient.Assets.Wis;
using MirClient.Assets.Wzl;

namespace MirClient.Assets.Wil;

public sealed class WilImageCache : IDisposable
{
    private readonly object _gate = new();
    private readonly Dictionary<WilImageKey, Task<WilImage?>> _inflight;
    private readonly Dictionary<string, WilArchive> _archives;
    private readonly LruCache<WilImageKey, WilImage> _images;
    private readonly SemaphoreSlim _decodeLimiter;
    private bool _disposed;

    public WilImageCache(WilImageCacheOptions? options = null)
    {
        options ??= new WilImageCacheOptions();

        int maxConcurrentDecodes = options.MaxConcurrentDecodes;
        if (maxConcurrentDecodes <= 0)
            maxConcurrentDecodes = Math.Clamp(Environment.ProcessorCount / 2, 1, 4);
        _decodeLimiter = new SemaphoreSlim(maxConcurrentDecodes, maxConcurrentDecodes);

        _inflight = new Dictionary<WilImageKey, Task<WilImage?>>(WilImageKeyComparer.Instance);
        _archives = new Dictionary<string, WilArchive>(StringComparer.OrdinalIgnoreCase);
        _images = new LruCache<WilImageKey, WilImage>(
            budgetWeight: options.ImageCacheBytes,
            weight: static img => img.Bgra32.LongLength,
            onEvict: null,
            comparer: WilImageKeyComparer.Instance);
    }

    public LruCacheStats ImageCacheStats => _images.Stats;

    public Task<WilImage?> GetImageAsync(string wilPath, int imageIndex)
    {
        if (string.IsNullOrWhiteSpace(wilPath))
            throw new ArgumentException("WIL path is required.", nameof(wilPath));

        if (_disposed)
            throw new ObjectDisposedException(nameof(WilImageCache));

        return GetImageAsyncFullPath(Path.GetFullPath(wilPath), imageIndex);
    }

    public bool TryGetImage(string wilPath, int imageIndex, out WilImage image)
    {
        if (string.IsNullOrWhiteSpace(wilPath))
            throw new ArgumentException("WIL path is required.", nameof(wilPath));

        if (_disposed)
            throw new ObjectDisposedException(nameof(WilImageCache));

        return TryGetImage(new WilImageKey(Path.GetFullPath(wilPath), imageIndex), out image);
    }

    public bool TryGetImage(WilImageKey key, out WilImage image)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WilImageCache));

        return _images.TryGet(key, out image);
    }

    public Task<WilImage?> GetImageAsyncFullPath(string fullWilPath, int imageIndex)
    {
        if (string.IsNullOrWhiteSpace(fullWilPath))
            throw new ArgumentException("WIL path is required.", nameof(fullWilPath));

        if (_disposed)
            throw new ObjectDisposedException(nameof(WilImageCache));

        var key = new WilImageKey(fullWilPath, imageIndex);

        if (_images.TryGet(key, out WilImage cached))
            return Task.FromResult<WilImage?>(cached);

        lock (_gate)
        {
            if (_images.TryGet(key, out cached))
                return Task.FromResult<WilImage?>(cached);

            if (_inflight.TryGetValue(key, out Task<WilImage?>? inflight))
                return inflight;

            Task<WilImage?> task = Task.Run(async () =>
            {
                await _decodeLimiter.WaitAsync().ConfigureAwait(false);
                try
                {
                    return DecodeAndCache(fullWilPath, imageIndex);
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

    public async Task PrefetchAsync(string wilPath, IEnumerable<int> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);

        List<Task> tasks = new();
        foreach (int index in indices)
        {
            tasks.Add(GetImageAsync(wilPath, index));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    public void ClearImages() => _images.Clear();

    public void Dispose()
    {
        Task<WilImage?>[] pending;
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
            foreach (WilArchive archive in _archives.Values)
                archive.Dispose();

            _archives.Clear();
        }
    }

    private WilImage? DecodeAndCache(string fullWilPath, int imageIndex)
    {
        var key = new WilImageKey(fullWilPath, imageIndex);
        try
        {
            if (_disposed)
                return null;

            WilArchive archive = GetOrOpenArchive(fullWilPath);

            if (!archive.TryDecodeImage(imageIndex, out WilImage image))
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

    private WilArchive GetOrOpenArchive(string fullWilPath)
    {
        lock (_gate)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(WilImageCache));

            if (_archives.TryGetValue(fullWilPath, out WilArchive? archive))
                return archive;

            archive = new WilArchive(fullWilPath);
            _archives.Add(fullWilPath, archive);
            return archive;
        }
    }

    private sealed class WilArchive : IDisposable
    {
        private readonly object _decodeGate = new();
        private readonly IImageFile _file;

        public WilArchive(string wilPath)
        {
            _file = OpenFile(wilPath);
        }

        public bool TryDecodeImage(int index, out WilImage image)
        {
            lock (_decodeGate)
            {
                return _file.TryDecodeImage(index, out image);
            }
        }

        public void Dispose() => _file.Dispose();

        private static IImageFile OpenFile(string path)
        {
            if (path.EndsWith(".wis", StringComparison.OrdinalIgnoreCase))
                return new WisImageFile(WisFile.Open(path));

            if (path.EndsWith(".wzl", StringComparison.OrdinalIgnoreCase))
                return new WzlImageFile(WzlFile.Open(path));

            return new WilImageFile(WilFile.Open(path));
        }

        private interface IImageFile : IDisposable
        {
            bool TryDecodeImage(int index, out WilImage image);
        }

        private sealed class WilImageFile : IImageFile
        {
            private readonly WilFile _file;

            public WilImageFile(WilFile file) => _file = file;

            public bool TryDecodeImage(int index, out WilImage image) => _file.TryDecodeImage(index, out image);

            public void Dispose() => _file.Dispose();
        }

        private sealed class WisImageFile : IImageFile
        {
            private readonly WisFile _file;

            public WisImageFile(WisFile file) => _file = file;

            public bool TryDecodeImage(int index, out WilImage image) => _file.TryDecodeImage(index, out image);

            public void Dispose() => _file.Dispose();
        }

        private sealed class WzlImageFile : IImageFile
        {
            private readonly WzlFile _file;

            public WzlImageFile(WzlFile file) => _file = file;

            public bool TryDecodeImage(int index, out WilImage image) => _file.TryDecodeImage(index, out image);

            public void Dispose() => _file.Dispose();
        }
    }
}
