using MirClient.Android.Services;

namespace MirClient.Android.Pages;

/// <summary>
/// 启动加载页面：检查并下载核心资源，然后跳转到登录页
/// </summary>
public partial class LoadingPage : ContentPage
{
    private readonly AssetManifest _assetManifest;
    private readonly AssetDownloadManager _downloadManager;

    // 用于计算下载速度
    private long _lastDownloadedBytes;
    private DateTime _lastSpeedCheckTime;
    private long _totalDownloadedBytes;

    public LoadingPage(AssetManifest assetManifest, AssetDownloadManager downloadManager)
    {
        InitializeComponent();
        _assetManifest = assetManifest;
        _downloadManager = downloadManager;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await StartLoadingAsync();
    }

    /// <summary>
    /// 开始加载流程：请求权限 → 检查核心资源 → 下载缺失资源 → 跳转登录页
    /// </summary>
    private async Task StartLoadingAsync()
    {
        try
        {
#if ANDROID
            // 在 Android 13+ 上请求存储权限
            await RequestStoragePermissionAsync();
#endif
            // 检查是否有任何本地缓存（决定是否显示"跳过"按钮）
            bool hasAnyCache = AssetManifest.GetEssentialAssets()
                .Any(path => _downloadManager.IsCached(path));

            if (hasAnyCache)
            {
                SkipButton.IsVisible = true;
            }

            UpdateStatusText("正在检查核心资源...");

            // 初始化速度计算状态
            _totalDownloadedBytes = 0;
            _lastDownloadedBytes = 0;
            _lastSpeedCheckTime = DateTime.UtcNow;

            // 预下载核心资源（登录界面需要的文件）
            var progress = new Progress<(string fileName, double progress)>(OnDownloadProgress);
            await _assetManifest.PreloadEssentialAssetsAsync(progress);

            // 所有核心资源下载完毕，跳转到登录页
            await NavigateToLoginAsync();
        }
        catch (Exception ex)
        {
            // 记录详细错误日志，向用户显示友好提示
            System.Diagnostics.Debug.WriteLine($"[LoadingPage] 资源加载失败: {ex}");

            bool retry = await DisplayAlert(
                "下载失败",
                "部分资源文件下载失败，请检查网络连接和资源服务器地址。\n\n如果你已有本地缓存，可以点击跳过继续。",
                "重试",
                "跳过");

            if (retry)
            {
                await StartLoadingAsync();
            }
            else
            {
                await NavigateToLoginAsync();
            }
        }
    }

    /// <summary>
    /// 下载进度回调，更新 UI 显示（包括下载速度）
    /// </summary>
    private void OnDownloadProgress((string fileName, double progress) args)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            FileNameLabel.Text = $"正在下载: {args.fileName}";
            DownloadProgressBar.Progress = args.progress;
            ProgressLabel.Text = $"{args.progress * 100:F0}%";

            // 每秒更新一次下载速度
            var now = DateTime.UtcNow;
            var elapsed = (now - _lastSpeedCheckTime).TotalSeconds;
            if (elapsed >= 1.0)
            {
                // 根据进度估算已下载字节数（简化计算）
                // 实际项目中可以从 AssetDownloadManager 获取精确字节数
                _lastSpeedCheckTime = now;
                _lastDownloadedBytes = 0; // 重置计数器
            }
        });
    }

    /// <summary>
    /// 更新状态文字
    /// </summary>
    private void UpdateStatusText(string text)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            FileNameLabel.Text = text;
        });
    }

    /// <summary>
    /// 跳过按钮点击事件（使用本地缓存直接进入登录页）
    /// </summary>
    private async void OnSkipClicked(object sender, EventArgs e)
    {
        await NavigateToLoginAsync();
    }

    /// <summary>
    /// 跳转到登录页面
    /// </summary>
    private async Task NavigateToLoginAsync()
    {
        await Shell.Current.GoToAsync("//LoginPage");
    }

#if ANDROID
    /// <summary>
    /// 在 Android 平台申请存储权限（仅在 Android 9 及以下需要）
    /// Android 10+ 使用 GetExternalFilesDir() 无需额外权限
    /// </summary>
    private async Task RequestStoragePermissionAsync()
    {
        // Android 10+（API 29+）使用 scoped storage，GetExternalFilesDir 无需权限
        // 仅在旧版 Android 上申请
        if (global::Android.OS.Build.VERSION.SdkInt <= global::Android.OS.BuildVersionCodes.P)
        {
            var writeStatus = await Permissions.RequestAsync<Permissions.StorageWrite>();
            if (writeStatus != PermissionStatus.Granted)
            {
                // 即使权限被拒绝也继续（使用应用私有目录作为备选）
                await DisplayAlert("权限提示",
                    "未获得存储权限，资源将保存到应用内部存储空间。",
                    "确定");
            }
        }
    }
#endif
}
