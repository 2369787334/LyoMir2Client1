using MirClient.Android.Controls;
using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

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
    }
}
