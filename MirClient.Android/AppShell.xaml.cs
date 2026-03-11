namespace MirClient.Android;

/// <summary>
/// 应用导航外壳，管理页面间的路由跳转
/// </summary>
public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();
// 传奇手机端 Shell 导航代码后台
namespace MirClient.Android;

/// <summary>
/// Shell 导航控制器，负责页面路由注册
/// </summary>
public partial class AppShell : Shell
{
    /// <summary>
    /// 构造函数，注册页面路由
    /// </summary>
    public AppShell()
    {
        InitializeComponent();

        // 注册游戏页面路由（从登录页面跳转）
        Routing.RegisterRoute(nameof(Pages.GamePage), typeof(Pages.GamePage));
    }
}
