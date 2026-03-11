using SkiaSharp;
using SkiaSharp.Views.Maui;
using SkiaSharp.Views.Maui.Controls;

namespace MirClient.Android.Controls;

/// <summary>
/// 摇杆移动事件参数
/// </summary>
public class JoystickMovedEventArgs : EventArgs
{
    /// <summary>X 轴方向，范围 [-1.0, 1.0]，负值向左，正值向右</summary>
    public float X { get; }

    /// <summary>Y 轴方向，范围 [-1.0, 1.0]，负值向上，正值向下</summary>
    public float Y { get; }

    /// <summary>摇杆是否处于中心（未移动）</summary>
    public bool IsCentered => MathF.Abs(X) < 0.01f && MathF.Abs(Y) < 0.01f;

    public JoystickMovedEventArgs(float x, float y)
    {
        X = x;
        Y = y;
    }
}

/// <summary>
/// 虚拟摇杆控件
/// 使用 SkiaSharp 绘制，支持触摸拖动控制方向
/// </summary>
public class VirtualJoystick : SKCanvasView
{
    // 摇杆移动事件
    public event EventHandler<JoystickMovedEventArgs>? JoystickMoved;

    // 外圆半径（背景圆）
    private float _outerRadius;

    // 内圆半径（摇杆手柄）
    private float _innerRadius;

    // 摇杆中心位置（控件中心）
    private SKPoint _center;

    // 当前摇杆手柄位置
    private SKPoint _stickPosition;

    // 是否正在被触摸
    private bool _isTouching;

    // 当前触摸点 ID（用于多指识别）
    private long _activeTouchId = -1;

    public VirtualJoystick()
    {
        EnableTouchEvents = true;
        Touch += OnTouch;
    }

    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        var canvas = e.Surface.Canvas;
        var info = e.Info;

        canvas.Clear(SKColors.Transparent);

        // 计算控件中心和半径
        _center = new SKPoint(info.Width / 2f, info.Height / 2f);
        _outerRadius = Math.Min(info.Width, info.Height) / 2f - 4f;
        _innerRadius = _outerRadius * 0.38f;

        // 如果未触摸，手柄回到中心
        if (!_isTouching)
        {
            _stickPosition = _center;
        }

        // 绘制外圆背景
        using var outerPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 255, 255, 40),
            Style = SKPaintStyle.Fill
        };
        canvas.DrawCircle(_center, _outerRadius, outerPaint);

        // 绘制外圆边框
        using var outerBorderPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(255, 255, 255, 100),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };
        canvas.DrawCircle(_center, _outerRadius, outerBorderPaint);

        // 绘制摇杆手柄
        using var innerPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(200, 200, 255, 180),
            Style = SKPaintStyle.Fill
        };
        canvas.DrawCircle(_stickPosition, _innerRadius, innerPaint);

        // 绘制手柄边框
        using var innerBorderPaint = new SKPaint
        {
            IsAntialias = true,
            Color = new SKColor(150, 150, 255, 220),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };
        canvas.DrawCircle(_stickPosition, _innerRadius, innerBorderPaint);
    }

    /// <summary>
    /// 触摸事件处理
    /// </summary>
    private void OnTouch(object? sender, SKTouchEventArgs e)
    {
        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
                // 记录触摸点 ID，支持多指时只响应第一个
                if (_activeTouchId < 0)
                {
                    _activeTouchId = e.Id;
                    _isTouching = true;
                    UpdateStickPosition(e.Location);
                }
                break;

            case SKTouchAction.Moved:
                if (e.Id == _activeTouchId)
                {
                    UpdateStickPosition(e.Location);
                }
                break;

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                if (e.Id == _activeTouchId)
                {
                    _activeTouchId = -1;
                    _isTouching = false;
                    _stickPosition = _center;

                    // 通知方向归零
                    JoystickMoved?.Invoke(this, new JoystickMovedEventArgs(0f, 0f));
                    InvalidateSurface();
                }
                break;
        }

        e.Handled = true;
    }

    /// <summary>
    /// 根据触摸位置更新摇杆手柄位置，并触发方向事件
    /// </summary>
    /// <param name="touchPoint">触摸点坐标（SkiaSharp 画布坐标）</param>
    private void UpdateStickPosition(SKPoint touchPoint)
    {
        // 计算触摸点相对于摇杆中心的偏移
        var delta = touchPoint - _center;
        var distance = delta.Length;

        if (distance > _outerRadius)
        {
            // 超出外圆范围，将手柄限制在外圆边缘
            var normalized = new SKPoint(delta.X / distance, delta.Y / distance);
            _stickPosition = _center + new SKPoint(normalized.X * _outerRadius, normalized.Y * _outerRadius);
        }
        else
        {
            _stickPosition = touchPoint;
        }

        // 计算归一化方向向量（范围 -1 到 1）
        var dirX = (_stickPosition.X - _center.X) / _outerRadius;
        var dirY = (_stickPosition.Y - _center.Y) / _outerRadius;

        // 触发摇杆移动事件
        JoystickMoved?.Invoke(this, new JoystickMovedEventArgs(dirX, dirY));

        // 重绘摇杆
        InvalidateSurface();
    }
}
