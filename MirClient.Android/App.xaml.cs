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
// 传奇手机端 App 主类
namespace MirClient.Android;

/// <summary>
/// 应用程序主类，负责初始化主窗口
/// </summary>
public partial class App : Application
{
    /// <summary>
    /// 构造函数，创建主 Shell 页面
    /// </summary>
    public App()
    {
        InitializeComponent();
        // 设置主导航 Shell
        MainPage = new AppShell();
    }
}
