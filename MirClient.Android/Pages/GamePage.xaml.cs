using MirClient.Android.Controls;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;
// 传奇手机端游戏渲染页面代码后台
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace MirClient.Android.Pages;

/// <summary>
/// 游戏主渲染页面
/// 使用 SkiaSharp 绘制游戏画面，提供虚拟摇杆和技能按钮
/// </summary>
public partial class GamePage : ContentPage
{
    private VirtualJoystick? _joystick;

    // 玩家移动方向（由摇杆控制）
    private float _moveX;
    private float _moveY;

    public GamePage()
    {
        InitializeComponent();
        SetupJoystick();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        // 开始游戏循环（每帧刷新渲染）
        Dispatcher.StartTimer(TimeSpan.FromMilliseconds(16), OnGameTick);
    }

    /// <summary>
    /// 初始化虚拟摇杆控件
    /// </summary>
    private void SetupJoystick()
    {
        _joystick = new VirtualJoystick
        {
            WidthRequest = 150,
            HeightRequest = 150
        };

        // 监听摇杆移动事件
        _joystick.JoystickMoved += OnJoystickMoved;

        JoystickFrame.Content = _joystick;
    }

    /// <summary>
    /// 游戏主循环 Tick，触发重绘
    /// </summary>
    private bool OnGameTick()
    {
        GameCanvas.InvalidateSurface();
        return true; // 返回 true 继续循环
    }

    /// <summary>
    /// SkiaSharp 绘制事件处理
    /// </summary>
    private void OnPaintSurface(object? sender, SKPaintSurfaceEventArgs e)
/// 游戏渲染页面，使用 SkiaSharp 作为渲染画布
/// 替代 PC 端的 Direct3D11 渲染层
/// </summary>
public partial class GamePage : ContentPage
{
    // 当前玩家移动方向向量（由摇杆控制）
    private float _moveX = 0f;
    private float _moveY = 0f;

    /// <summary>
    /// 构造函数
    /// </summary>
    public GamePage()
    {
        InitializeComponent();
    }

    /// <summary>
    /// SkiaSharp 画布绘制回调
    /// 每帧调用此方法进行游戏画面渲染
    /// </summary>
    private void OnGameCanvasPaintSurface(object sender, SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;

        // 清空画布（黑色背景）
        canvas.Clear(SKColors.Black);

        // 绘制占位文字
        using var textPaint = new SKPaint
        {
            Color = new SKColor(100, 100, 150),
            TextSize = 28,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center
        };

        canvas.DrawText("游戏画面加载中...", info.Width / 2f, info.Height / 2f, textPaint);

        // TODO: 在这里接入 MirClient 游戏渲染逻辑
        // 当前为占位实现，后续版本将渲染实际游戏地图和角色
    }

    /// <summary>
    /// 画布触摸事件（传递给游戏逻辑处理）
    /// </summary>
    private void OnCanvasTouch(object? sender, SKTouchEventArgs e)
    {
        // TODO: 处理游戏区域的触摸交互（如点击目标、移动等）
        e.Handled = true;
    }

    /// <summary>
    /// 摇杆移动事件处理
    /// </summary>
    private void OnJoystickMoved(object? sender, JoystickMovedEventArgs e)
        // 清除画布，填充黑色背景
        canvas.Clear(SKColors.Black);

        // === 占位渲染：显示加载提示文字 ===
        using var textPaint = new SKPaint
        {
            Color = SKColors.White,
            TextSize = 28,
            IsAntialias = true,
            TextAlign = SKTextAlign.Center,
        };

        // 在屏幕中央绘制提示文字
        float centerX = info.Width / 2f;
        float centerY = info.Height / 2f;
        canvas.DrawText("游戏画面加载中...", centerX, centerY, textPaint);

        // 绘制版本水印
        using var versionPaint = new SKPaint
        {
            Color = new SKColor(100, 100, 100),
            TextSize = 14,
            IsAntialias = true,
            TextAlign = SKTextAlign.Right,
        };
        canvas.DrawText("LyoMir2Client Android v1.0", info.Width - 10, info.Height - 200, versionPaint);

        // TODO: 实际游戏画面渲染逻辑在此扩展
        // 例如：调用 MirClient.Core 的渲染接口
    }

    /// <summary>
    /// 虚拟摇杆移动事件处理
    /// 接收方向向量并更新移动状态
    /// </summary>
    private void OnJoystickMoved(object? sender, Controls.JoystickEventArgs e)
    {
        _moveX = e.X;
        _moveY = e.Y;

        // TODO: 将移动向量传递给游戏逻辑
    }

    /// <summary>
    /// 技能按钮按下事件
    /// </summary>
    private void OnSkillButtonPressed(object? sender, EventArgs e)
    {
        if (sender is Button btn && btn.CommandParameter is string skillIdStr
            && int.TryParse(skillIdStr, out int skillId))
        {
            // TODO: 触发技能释放逻辑
            System.Diagnostics.Debug.WriteLine($"技能 {skillId} 被触发");
        }
        // 更新 HUD 方向显示
        LabelDirection.Text = $"方向: ({_moveX:F2}, {_moveY:F2})";

        // TODO: 将移动方向发送给游戏逻辑层
        // 例如：调用 MirClient.Core 的移动接口
    }

    // === 技能按钮点击事件 ===

    /// <summary>技能1 按钮点击</summary>
    private void OnSkill1Clicked(object sender, EventArgs e)
    {
        // TODO: 触发技能1逻辑
    }

    /// <summary>技能2 按钮点击</summary>
    private void OnSkill2Clicked(object sender, EventArgs e)
    {
        // TODO: 触发技能2逻辑
    }

    /// <summary>技能3 按钮点击</summary>
    private void OnSkill3Clicked(object sender, EventArgs e)
    {
        // TODO: 触发技能3逻辑
    }

    /// <summary>技能4 按钮点击</summary>
    private void OnSkill4Clicked(object sender, EventArgs e)
    {
        // TODO: 触发技能4逻辑
    }
}
