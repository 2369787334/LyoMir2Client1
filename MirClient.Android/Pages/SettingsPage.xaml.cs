using MirClient.Android.Services;

namespace MirClient.Android.Pages;

/// <summary>
/// 设置页面：配置资源服务器地址、管理本地缓存、查看版本信息
/// </summary>
public partial class SettingsPage : ContentPage
{
    private readonly AssetDownloadManager _downloadManager;

    // 首选项存储键
    private const string PrefKeyAssetServer = "asset_server";

    public SettingsPage(AssetDownloadManager downloadManager)
    {
        InitializeComponent();
        _downloadManager = downloadManager;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // 加载保存的资源服务器地址
        AssetServerEntry.Text = Preferences.Get(PrefKeyAssetServer, _downloadManager.BaseUrl);

        // 显示缓存目录路径
        CacheDirLabel.Text = _downloadManager.CacheDir;

        // 计算并显示缓存大小
        RefreshCacheSize();
    }

    /// <summary>
    /// 刷新缓存大小显示
    /// </summary>
    private void RefreshCacheSize()
    {
        Task.Run(() =>
        {
            var sizeBytes = _downloadManager.GetCacheSize();
            var sizeStr = FormatFileSize(sizeBytes);

            MainThread.BeginInvokeOnMainThread(() =>
            {
                CacheSizeLabel.Text = sizeStr;
            });
        });
    }

    /// <summary>
    /// 将字节数格式化为易读的大小字符串
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        if (bytes < 1024 * 1024 * 1024) return $"{bytes / 1024.0 / 1024.0:F1} MB";
        return $"{bytes / 1024.0 / 1024.0 / 1024.0:F2} GB";
    }

    /// <summary>
    /// 保存资源服务器地址
    /// </summary>
    private async void OnSaveServerClicked(object sender, EventArgs e)
    {
        var url = AssetServerEntry.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(url))
        {
            await DisplayAlert("提示", "请输入资源服务器地址", "确定");
            return;
        }

        // 确保 URL 以斜杠结尾
        if (!url.EndsWith('/'))
            url += "/";

        // 保存到首选项和下载管理器
        Preferences.Set(PrefKeyAssetServer, url);
        _downloadManager.BaseUrl = url;

        await DisplayAlert("已保存", $"资源服务器地址已更新为：\n{url}", "确定");
    }

    /// <summary>
    /// 清理本地缓存
    /// </summary>
    private async void OnClearCacheClicked(object sender, EventArgs e)
    {
        var sizeStr = CacheSizeLabel.Text;
        bool confirm = await DisplayAlert(
            "清理缓存",
            $"确定要清理所有本地缓存（{sizeStr}）吗？\n\n清理后再次进入游戏需要重新下载资源文件。",
            "确定清理",
            "取消");

        if (!confirm) return;

        _downloadManager.ClearCache();
        RefreshCacheSize();

        await DisplayAlert("完成", "缓存已清理", "确定");
    }
}
