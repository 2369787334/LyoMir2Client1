using MirClient.Android.Services;
using SkiaSharp.Views.Maui.Controls.Hosting;
// 传奇手机端 MAUI 应用启动入口
using Microsoft.Maui.Controls.Hosting;
using SkiaSharp.Views.Maui.Controls.Hosting;
using Plugin.Maui.Audio;

namespace MirClient.Android;

/// <summary>
/// MAUI 应用程序主入口，负责配置依赖注入和服务
/// </summary>
public static class MauiProgram
{
/// MAUI 应用程序构建入口类
/// </summary>
public static class MauiProgram
{
    /// <summary>
    /// 创建并配置 MAUI 应用实例
    /// </summary>
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            // 注册 SkiaSharp 渲染支持
            .UseSkiaSharp()
            // 注册主 App 类
            .UseMauiApp<App>()
            // 注册 SkiaSharp 渲染支持（用于游戏画布，替代 Direct3D11）
            .UseSkiaSharp()
            // 配置默认字体
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // 注册资源下载管理器（单例）
        builder.Services.AddSingleton<AssetDownloadManager>();

        // 注册资源清单服务（单例）
        builder.Services.AddSingleton<AssetManifest>();

        // 注册各页面（瞬态，每次导航创建新实例）
        builder.Services.AddTransient<Pages.LoadingPage>();
        builder.Services.AddTransient<Pages.LoginPage>();
        builder.Services.AddTransient<Pages.GamePage>();
        builder.Services.AddTransient<Pages.SettingsPage>();

#if DEBUG
        builder.Logging.AddDebug();
#endif
        // 注册音频服务（替代 DirectSound）
        builder.Services.AddSingleton(AudioManager.Current);

        return builder.Build();
    }
}
