namespace MirClient.Android;

/// <summary>
/// 应用程序主类，设置初始页面为加载页
/// </summary>
public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // 启动时显示 AppShell，由 AppShell 控制导航流程
        MainPage = new AppShell();
    }
}
