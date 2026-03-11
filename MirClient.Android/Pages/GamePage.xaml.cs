// 传奇手机端游戏渲染页面代码后台
using SkiaSharp;
using SkiaSharp.Views.Maui;

namespace MirClient.Android.Pages;

/// <summary>
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
