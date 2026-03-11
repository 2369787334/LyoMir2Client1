using System.Text.Json;
using System.Text.Json.Serialization;

namespace MirClient.Android.Services;

/// <summary>
/// 单个资源文件的清单记录
/// </summary>
public class AssetFileEntry
{
    /// <summary>相对路径，如 "Data/Prguse.wil"</summary>
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    /// <summary>文件大小（字节）</summary>
    [JsonPropertyName("size")]
    public long Size { get; set; }

    /// <summary>文件 MD5 校验值（小写十六进制）</summary>
    [JsonPropertyName("md5")]
    public string Md5 { get; set; } = "";
}

/// <summary>
/// 资源清单数据，对应服务器上的 manifest.json 文件
/// </summary>
public class AssetManifestData
{
    /// <summary>资源包版本号</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    /// <summary>所有资源文件列表</summary>
    [JsonPropertyName("files")]
    public List<AssetFileEntry> Files { get; set; } = new();
}

/// <summary>
/// 资源清单管理服务
/// 负责从服务器获取资源清单，对比本地文件，管理预加载
/// </summary>
public class AssetManifest
{
    // 登录界面必须的核心资源（启动时优先下载）
    private static readonly string[] EssentialAssets =
    [
        "Data/Prguse.wil",
        "Data/ChrSel.wil",
        "Data/Login.wil",
    ];

    private readonly AssetDownloadManager _downloadManager;
    private AssetManifestData? _cachedManifest;

    public AssetManifest(AssetDownloadManager downloadManager)
    {
        _downloadManager = downloadManager;
    }

    /// <summary>
    /// 从服务器获取最新的资源清单
    /// </summary>
    /// <returns>资源清单数据</returns>
    public async Task<AssetManifestData> FetchManifestAsync()
    {
        var manifestUrl = _downloadManager.BaseUrl.TrimEnd('/') + "/manifest.json";

        // 复用 AssetDownloadManager 的 HttpClient，避免 socket 泄漏
        var json = await _downloadManager.HttpClient.GetStringAsync(manifestUrl);

        _cachedManifest = JsonSerializer.Deserialize<AssetManifestData>(json)
                          ?? throw new InvalidDataException("无法解析资源清单文件");

        return _cachedManifest;
    }

    /// <summary>
    /// 对比本地缓存，返回需要更新或下载的文件相对路径列表
    /// </summary>
    /// <param name="manifest">从服务器获取的最新清单</param>
    /// <returns>需要下载的文件路径列表</returns>
    public List<string> GetOutdatedFiles(AssetManifestData manifest)
    {
        var outdated = new List<string>();

        foreach (var entry in manifest.Files)
        {
            var localPath = _downloadManager.GetLocalPath(entry.Path);

            if (!File.Exists(localPath))
            {
                // 本地不存在，需要下载
                outdated.Add(entry.Path);
                continue;
            }

            var fileInfo = new FileInfo(localPath);

            // 检查文件大小是否一致
            if (fileInfo.Length != entry.Size)
            {
                outdated.Add(entry.Path);
                continue;
            }

            // 如果提供了 MD5，进一步校验文件完整性
            if (!string.IsNullOrEmpty(entry.Md5))
            {
                var localMd5 = AssetDownloadManager.ComputeMd5(localPath);
                if (!string.Equals(localMd5, entry.Md5, StringComparison.OrdinalIgnoreCase))
                {
                    outdated.Add(entry.Path);
                }
            }
        }

        return outdated;
    }

    /// <summary>
    /// 预下载登录界面所需的核心资源文件
    /// </summary>
    /// <param name="progress">进度回调，传入当前文件名和该文件进度（0.0 ~ 1.0）</param>
    public async Task PreloadEssentialAssetsAsync(IProgress<(string fileName, double progress)>? progress = null)
    {
        for (int i = 0; i < EssentialAssets.Length; i++)
        {
            var assetPath = EssentialAssets[i];

            // 如果已有缓存，跳过下载
            if (_downloadManager.IsCached(assetPath))
            {
                progress?.Report((assetPath, 1.0));
                continue;
            }

            // 创建该文件的进度报告器
            var fileProgress = new Progress<double>(p =>
            {
                progress?.Report((assetPath, p));
            });

            await _downloadManager.EnsureFileAsync(assetPath, fileProgress);
        }
    }

    /// <summary>
    /// 获取核心资源列表（登录界面必需）
    /// </summary>
    public static IReadOnlyList<string> GetEssentialAssets() => EssentialAssets;
}
