using MirClient.Android.Services;
using SkiaSharp.Views.Maui.Controls.Hosting;

namespace MirClient.Android;

/// <summary>
/// MAUI 应用程序主入口，负责配置依赖注入和服务
/// </summary>
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            // 注册 SkiaSharp 渲染支持
            .UseSkiaSharp()
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

        return builder.Build();
    }
}
