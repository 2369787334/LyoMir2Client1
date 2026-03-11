using MirClient.Android.Services;

namespace MirClient.Android.Pages;

/// <summary>
/// 登录页面：输入服务器地址、账号、密码，点击登录进入游戏
/// </summary>
public partial class LoginPage : ContentPage
{
    private readonly AssetDownloadManager _downloadManager;

    // 首选项存储键
    private const string PrefKeyServer = "game_server";
    private const string PrefKeyAccount = "last_account";

    public LoginPage(AssetDownloadManager downloadManager)
    {
        InitializeComponent();
        _downloadManager = downloadManager;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        LoadSavedPreferences();
    }

    /// <summary>
    /// 加载上次保存的服务器地址和账号
    /// </summary>
    private void LoadSavedPreferences()
    {
        ServerEntry.Text = Preferences.Get(PrefKeyServer, "192.168.1.1:5000");
        AccountEntry.Text = Preferences.Get(PrefKeyAccount, "");
    }

    /// <summary>
    /// 登录按钮点击事件
    /// </summary>
    private async void OnLoginClicked(object sender, EventArgs e)
    {
        var server = ServerEntry.Text?.Trim() ?? "";
        var account = AccountEntry.Text?.Trim() ?? "";
        var password = PasswordEntry.Text ?? "";

        // 基本输入校验
        if (string.IsNullOrEmpty(server))
        {
            StatusLabel.Text = "请输入服务器地址";
            return;
        }

        if (string.IsNullOrEmpty(account))
        {
            StatusLabel.Text = "请输入账号";
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            StatusLabel.Text = "请输入密码";
            return;
        }

        // 保存服务器地址和账号到首选项
        Preferences.Set(PrefKeyServer, server);
        Preferences.Set(PrefKeyAccount, account);

        StatusLabel.Text = "正在连接服务器...";
        StatusLabel.TextColor = Color.FromArgb("#aaaacc");

        // TODO: 在这里调用 MirClient.Net 的连接逻辑
        // 目前仅做占位演示，直接跳转到游戏页面
        await Task.Delay(500); // 模拟连接延迟

        // 跳转到游戏主页面
        await Shell.Current.GoToAsync("//GamePage");
    }

    /// <summary>
    /// 设置按钮点击事件：跳转到设置页
    /// </summary>
    private async void OnSettingsClicked(object sender, EventArgs e)
    {
        await Shell.Current.GoToAsync("//SettingsPage");
    }
}
