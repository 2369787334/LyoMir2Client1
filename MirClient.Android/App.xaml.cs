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
