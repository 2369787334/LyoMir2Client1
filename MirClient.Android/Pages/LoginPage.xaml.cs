// 传奇手机端登录页面代码后台
using System.Net.Sockets;

namespace MirClient.Android.Pages;

/// <summary>
/// 登录页面，负责服务器连接和账号验证
/// </summary>
public partial class LoginPage : ContentPage
{
    /// <summary>
    /// 构造函数
    /// </summary>
    public LoginPage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// 登录按钮点击事件处理
    /// 使用 TcpClient 测试与服务器的连接
    /// </summary>
    private async void OnLoginClicked(object sender, EventArgs e)
    {
        // 禁用登录按钮，防止重复点击
        BtnLogin.IsEnabled = false;

        // 获取输入内容并去除空白字符
        string serverInput = EntryServer.Text?.Trim() ?? string.Empty;
        string username = EntryUsername.Text?.Trim() ?? string.Empty;
        string password = EntryPassword.Text ?? string.Empty;

        // 基本输入验证
        if (string.IsNullOrEmpty(serverInput))
        {
            LabelStatus.Text = "请输入服务器地址";
            LabelStatus.TextColor = Colors.Red;
            BtnLogin.IsEnabled = true;
            return;
        }

        if (string.IsNullOrEmpty(username))
        {
            LabelStatus.Text = "请输入账号";
            LabelStatus.TextColor = Colors.Red;
            BtnLogin.IsEnabled = true;
            return;
        }

        if (string.IsNullOrEmpty(password))
        {
            LabelStatus.Text = "请输入密码";
            LabelStatus.TextColor = Colors.Red;
            BtnLogin.IsEnabled = true;
            return;
        }

        // 解析服务器地址和端口
        string host;
        int port;

        try
        {
            // 支持 "host:port" 格式
            if (serverInput.Contains(':'))
            {
                var parts = serverInput.Split(':', 2);
                host = parts[0];
                port = int.Parse(parts[1]);
            }
            else
            {
                // 仅主机名时使用默认端口 7000
                host = serverInput;
                port = 7000;
            }
        }
        catch (FormatException)
        {
            LabelStatus.Text = "服务器地址格式错误，请使用 IP:端口 格式";
            LabelStatus.TextColor = Colors.Red;
            BtnLogin.IsEnabled = true;
            return;
        }
        catch (OverflowException)
        {
            LabelStatus.Text = "端口号超出范围，请使用 1~65535 之间的端口";
            LabelStatus.TextColor = Colors.Red;
            BtnLogin.IsEnabled = true;
            return;
        }

        // 显示连接中状态
        LabelStatus.Text = "连接中...";
        LabelStatus.TextColor = Color.FromArgb("#FFD700");

        // 在后台线程进行 TCP 连接测试
        bool connected = await Task.Run(async () =>
        {
            try
            {
                using var client = new TcpClient();
                // 设置 5 秒超时
                var connectTask = client.ConnectAsync(host, port);
                var timeoutTask = Task.Delay(5000);
                var completed = await Task.WhenAny(connectTask, timeoutTask);

                if (completed == timeoutTask)
                {
                    // 连接超时
                    return false;
                }

                // 检查连接是否成功（如有异常会在此抛出）
                await connectTask;
                return client.Connected;
            }
            catch (SocketException ex) when (ex.SocketErrorCode == SocketError.TimedOut ||
                                             ex.SocketErrorCode == SocketError.ConnectionRefused ||
                                             ex.SocketErrorCode == SocketError.HostNotFound ||
                                             ex.SocketErrorCode == SocketError.HostUnreachable ||
                                             ex.SocketErrorCode == SocketError.NetworkUnreachable)
            {
                // 网络层错误（连接拒绝/主机不可达等）
                return false;
            }
            catch (SocketException)
            {
                // 其他 Socket 错误
                return false;
            }
            catch (Exception)
            {
                // 其他未知错误（如 DNS 解析失败等）
                return false;
            }
        });

        if (connected)
        {
            // 连接成功，显示提示并跳转到游戏页面
            LabelStatus.Text = "连接成功！正在进入游戏...";
            LabelStatus.TextColor = Colors.LightGreen;

            // 短暂延迟后跳转到游戏页面
            await Task.Delay(500);
            await Shell.Current.GoToAsync(nameof(GamePage));
        }
        else
        {
            // 连接失败，显示错误信息
            LabelStatus.Text = $"连接失败：无法连接到 {host}:{port}，请检查地址或网络";
            LabelStatus.TextColor = Colors.Red;
        }

        // 恢复登录按钮
        BtnLogin.IsEnabled = true;
    }
}
