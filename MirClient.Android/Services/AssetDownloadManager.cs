using System.Security.Cryptography;

namespace MirClient.Android.Services;

/// <summary>
/// 资源下载管理器（单例模式）
/// 负责按需从HTTP服务器下载游戏资源文件，并管理本地缓存
/// </summary>
public class AssetDownloadManager
{
    // 最大并发下载数量
    private const int MaxConcurrentDownloads = 3;

    private readonly HttpClient _httpClient;
    private readonly SemaphoreSlim _downloadSemaphore;
    private readonly object _cacheLock = new();

    /// <summary>
    /// 共享的 HttpClient 实例（供外部服务复用，避免 socket 泄漏）
    /// </summary>
    public HttpClient HttpClient => _httpClient;

    /// <summary>
    /// 资源服务器基础地址（可在设置中修改）
    /// </summary>
    public string BaseUrl { get; set; } = "http://your-server.com/mir2/";

    /// <summary>
    /// 本地缓存目录路径
    /// 使用 Android 外部存储目录：/sdcard/LyoMir2/Cache/
    /// </summary>
    public string CacheDir { get; }

    public AssetDownloadManager()
    {
        // 初始化 HttpClient，设置超时时间
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        // 初始化并发下载信号量
        _downloadSemaphore = new SemaphoreSlim(MaxConcurrentDownloads, MaxConcurrentDownloads);

        // 设置缓存目录
        // Android 10+ 使用 GetExternalFilesDir 获取应用专属外部目录（无需存储权限）
        // 其他平台使用应用数据目录
#if ANDROID
        var externalDir = global::Android.App.Application.Context.GetExternalFilesDir(null)?.AbsolutePath
                          ?? FileSystem.AppDataDirectory;
        CacheDir = Path.Combine(externalDir, "LyoMir2Cache");
#else
        CacheDir = Path.Combine(FileSystem.AppDataDirectory, "LyoMir2", "Cache");
#endif

        // 确保缓存目录存在
        Directory.CreateDirectory(CacheDir);
    }

    /// <summary>
    /// 确保指定资源文件存在于本地缓存，如不存在则从服务器下载
    /// </summary>
    /// <param name="relativePath">相对于资源根目录的文件路径，例如 "Data/Prguse.wil"</param>
    /// <param name="progress">下载进度回调（0.0 ~ 1.0）</param>
    /// <returns>本地缓存文件的完整路径</returns>
    public async Task<string> EnsureFileAsync(string relativePath, IProgress<double>? progress = null)
    {
        var localPath = GetLocalPath(relativePath);

        // 检查本地缓存是否存在
        if (IsCached(relativePath))
        {
            progress?.Report(1.0);
            return localPath;
        }

        // 限制并发下载数量
        await _downloadSemaphore.WaitAsync();
        try
        {
            // 双重检查，防止重复下载
            if (IsCached(relativePath))
            {
                progress?.Report(1.0);
                return localPath;
            }

            await DownloadFileAsync(relativePath, localPath, progress);
            return localPath;
        }
        finally
        {
            _downloadSemaphore.Release();
        }
    }

    /// <summary>
    /// 检查指定文件是否已在本地缓存
    /// </summary>
    /// <param name="relativePath">相对路径</param>
    /// <returns>是否已缓存</returns>
    public bool IsCached(string relativePath)
    {
        var localPath = GetLocalPath(relativePath);
        return File.Exists(localPath) && new FileInfo(localPath).Length > 0;
    }

    /// <summary>
    /// 获取资源文件在本地缓存的完整路径
    /// </summary>
    /// <param name="relativePath">相对路径，例如 "Data/Prguse.wil"</param>
    /// <returns>本地完整路径</returns>
    public string GetLocalPath(string relativePath)
    {
        // 将路径分隔符统一为系统分隔符
        var normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar)
                                         .Replace('\\', Path.DirectorySeparatorChar);
        return Path.Combine(CacheDir, normalizedPath);
    }

    /// <summary>
    /// 清理所有本地缓存文件
    /// </summary>
    public void ClearCache()
    {
        lock (_cacheLock)
        {
            if (Directory.Exists(CacheDir))
            {
                Directory.Delete(CacheDir, recursive: true);
                Directory.CreateDirectory(CacheDir);
            }
        }
    }

    /// <summary>
    /// 获取当前缓存占用的总字节数
    /// </summary>
    /// <returns>缓存大小（字节）</returns>
    public long GetCacheSize()
    {
        if (!Directory.Exists(CacheDir))
            return 0;

        return Directory.GetFiles(CacheDir, "*", SearchOption.AllDirectories)
                        .Sum(file => new FileInfo(file).Length);
    }

    /// <summary>
    /// 从服务器下载文件到本地缓存，支持断点续传
    /// </summary>
    /// <param name="relativePath">相对路径</param>
    /// <param name="localPath">本地保存路径</param>
    /// <param name="progress">进度回调</param>
    private async Task DownloadFileAsync(string relativePath, string localPath, IProgress<double>? progress)
    {
        // 确保父目录存在
        var directory = Path.GetDirectoryName(localPath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // 构建下载 URL
        var url = BaseUrl.TrimEnd('/') + "/" + relativePath.Replace('\\', '/');

        // 临时文件路径，下载完成后重命名（防止下载中断导致文件损坏）
        var tempPath = localPath + ".tmp";

        try
        {
            // 检查是否有未完成的临时文件（支持断点续传）
            long existingLength = 0;
            if (File.Exists(tempPath))
            {
                existingLength = new FileInfo(tempPath).Length;
            }

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            // 添加 Range 请求头实现断点续传
            if (existingLength > 0)
            {
                request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(existingLength, null);
            }

            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

            // 如果服务器不支持断点续传，从头开始下载
            if (existingLength > 0 && response.StatusCode != System.Net.HttpStatusCode.PartialContent)
            {
                existingLength = 0;
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }

            response.EnsureSuccessStatusCode();

            var totalLength = (response.Content.Headers.ContentLength ?? 0) + existingLength;

            // 以追加模式打开文件流
            using var fileStream = new FileStream(tempPath, existingLength > 0 ? FileMode.Append : FileMode.Create);
            using var contentStream = await response.Content.ReadAsStreamAsync();

            var buffer = new byte[81920]; // 80 KB 缓冲区
            long downloaded = existingLength;
            int bytesRead;

            while ((bytesRead = await contentStream.ReadAsync(buffer)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead));
                downloaded += bytesRead;

                // 报告下载进度
                if (totalLength > 0)
                {
                    progress?.Report((double)downloaded / totalLength);
                }
            }

            await fileStream.FlushAsync();
        }
        catch (Exception ex)
        {
            // 下载失败时清理临时文件
            if (File.Exists(tempPath))
                File.Delete(tempPath);
            throw new IOException($"下载文件失败 [{relativePath}]: {ex.Message}", ex);
        }

        // 下载完成，将临时文件重命名为正式文件
        if (File.Exists(localPath))
            File.Delete(localPath);
        File.Move(tempPath, localPath);

        progress?.Report(1.0);
    }

    /// <summary>
    /// 计算文件的 MD5 哈希值（用于完整性校验）
    /// </summary>
    /// <param name="filePath">文件路径</param>
    /// <returns>小写十六进制 MD5 字符串</returns>
    public static string ComputeMd5(string filePath)
    {
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        var hash = md5.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
