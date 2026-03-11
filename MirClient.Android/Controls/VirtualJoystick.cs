// 传奇手机端虚拟摇杆控件
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
/// 摇杆事件参数，携带方向向量
/// </summary>
public class JoystickEventArgs : EventArgs
{
    /// <summary>水平方向分量，范围 -1.0 到 1.0（负为左，正为右）</summary>
    public float X { get; init; }

    /// <summary>垂直方向分量，范围 -1.0 到 1.0（负为上，正为下）</summary>
    public float Y { get; init; }
}

/// <summary>
/// 虚拟摇杆自定义控件（基于 SKCanvasView）
/// 支持触摸拖动，计算方向向量，触摸释放后自动归位
/// </summary>
public class VirtualJoystick : SKCanvasView
{
    // === 外观颜色常量 ===

    // 底盘填充色（半透明黑色）
    private static readonly SKColor BaseColor = new(0, 0, 0, 160);
    // 底盘边框色
    private static readonly SKColor BorderColor = new(255, 255, 255, 100);
    // 方向线颜色
    private static readonly SKColor DirectionLineColor = new(255, 255, 255, 80);
    // 手柄渐变起始色（亮红）
    private static readonly SKColor HandleGradientStart = new(255, 100, 100, 230);
    // 手柄渐变结束色（深红）
    private static readonly SKColor HandleGradientEnd = new(180, 20, 20, 200);
    // 手柄高光色
    private static readonly SKColor HandleHighlightColor = new(255, 255, 255, 60);
    // 手柄边框色
    private static readonly SKColor HandleBorderColor = new(255, 150, 150, 180);

    // === 尺寸比例常量 ===

    // 摇杆底盘半径（占控件最小边的比例）
    private const float BaseRadiusRatio = 0.42f;
    // 摇杆手柄半径（占控件最小边的比例）
    private const float HandleRadiusRatio = 0.20f;

    // === 状态字段 ===

    // 触摸是否按下
    private bool _isTouching = false;
    // 当前触摸点（相对于控件中心）
    private float _touchX = 0f;
    private float _touchY = 0f;
    // 当前方向向量（-1.0 到 1.0）
    private float _directionX = 0f;
    private float _directionY = 0f;

    /// <summary>
    /// 摇杆移动事件，触摸拖动时触发，传递方向向量
    /// </summary>
    public event EventHandler<JoystickEventArgs>? JoystickMoved;

    /// <summary>
    /// 构造函数，注册触摸事件
    /// </summary>
    public VirtualJoystick()
    {
        // 启用触摸事件支持
        EnableTouchEvents = true;
        Touch += OnTouchEvent;
    }

    /// <summary>
    /// SkiaSharp 绘制回调，渲染摇杆外观
    /// </summary>
    protected override void OnPaintSurface(SKPaintSurfaceEventArgs e)
    {
        base.OnPaintSurface(e);

        var canvas = e.Surface.Canvas;
        var info = e.Info;

        // 清除背景（透明）
        canvas.Clear(SKColors.Transparent);

        // 控件中心点
        float cx = info.Width / 2f;
        float cy = info.Height / 2f;

        // 按最小边计算半径（保持圆形）
        float minSide = Math.Min(info.Width, info.Height);
        float baseRadius = minSide * BaseRadiusRatio;
        float handleRadius = minSide * HandleRadiusRatio;

        // === 绘制底盘（外圆） ===
        using var basePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            // 半透明深色底盘
            Color = BaseColor,
        };
        canvas.DrawCircle(cx, cy, baseRadius, basePaint);

        // 底盘边框
        using var borderPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 3,
            Color = BorderColor,
        };
        canvas.DrawCircle(cx, cy, baseRadius, borderPaint);

        // === 绘制方向指示线 ===
        if (_isTouching && (_directionX != 0f || _directionY != 0f))
        {
            using var linePaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2,
                Color = DirectionLineColor,
            };
            // 绘制从中心到当前方向的延伸线
            float lineEndX = cx + _directionX * baseRadius;
            float lineEndY = cy + _directionY * baseRadius;
            canvas.DrawLine(cx, cy, lineEndX, lineEndY, linePaint);
        }

        // === 绘制摇杆手柄（内圆） ===
        // 手柄位置：触摸时跟随手指，未触摸时在中心
        float handleX = cx + (_isTouching ? _touchX : 0f);
        float handleY = cy + (_isTouching ? _touchY : 0f);

        // 手柄渐变色
        using var handleShader = SKShader.CreateRadialGradient(
            new SKPoint(handleX - handleRadius * 0.3f, handleY - handleRadius * 0.3f),
            handleRadius * 1.2f,
            [HandleGradientStart, HandleGradientEnd],
            null,
            SKShaderTileMode.Clamp);

        using var handlePaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Shader = handleShader,
        };
        canvas.DrawCircle(handleX, handleY, handleRadius, handlePaint);

        // 手柄高光
        using var highlightPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = HandleHighlightColor,
        };
        canvas.DrawCircle(handleX - handleRadius * 0.25f, handleY - handleRadius * 0.25f, handleRadius * 0.4f, highlightPaint);

        // 手柄边框
        using var handleBorderPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2,
            Color = HandleBorderColor,
        };
        canvas.DrawCircle(handleX, handleY, handleRadius, handleBorderPaint);
    }

    /// <summary>
    /// 触摸事件处理，计算方向向量并触发事件
    /// </summary>
    private void OnTouchEvent(object? sender, SKTouchEventArgs e)
    {
        // 控件中心（逻辑坐标）
        float cx = (float)(Width / 2.0);
        float cy = (float)(Height / 2.0);
        float minSide = (float)Math.Min(Width, Height);
        float baseRadius = minSide * BaseRadiusRatio;

        switch (e.ActionType)
        {
            case SKTouchAction.Pressed:
            case SKTouchAction.Moved:
                {
                    _isTouching = true;

                    // 计算触摸点相对于中心的偏移（逻辑坐标）
                    float dx = e.Location.X - cx;
                    float dy = e.Location.Y - cy;

                    // 计算距离
                    float distance = MathF.Sqrt(dx * dx + dy * dy);

                    if (distance < 0.001f)
                    {
                        // 触摸点在中心，不移动
                        _touchX = 0f;
                        _touchY = 0f;
                        _directionX = 0f;
                        _directionY = 0f;
                    }
                    else
                    {
                        // 将手柄限制在底盘范围内
                        float handleRadius = minSide * HandleRadiusRatio;
                        float maxOffset = baseRadius - handleRadius;
                        float clampedDistance = Math.Min(distance, maxOffset);

                        // 归一化方向
                        float normalizedX = dx / distance;
                        float normalizedY = dy / distance;

                        // 手柄实际偏移（像素）
                        _touchX = normalizedX * clampedDistance;
                        _touchY = normalizedY * clampedDistance;

                        // 方向向量（-1.0 到 1.0）
                        _directionX = normalizedX * Math.Min(distance / maxOffset, 1.0f);
                        _directionY = normalizedY * Math.Min(distance / maxOffset, 1.0f);
                    }

                    // 触发摇杆移动事件
                    JoystickMoved?.Invoke(this, new JoystickEventArgs
                    {
                        X = _directionX,
                        Y = _directionY,
                    });

                    // 请求重绘
                    InvalidateSurface();
                    e.Handled = true;
                    break;
                }

            case SKTouchAction.Released:
            case SKTouchAction.Cancelled:
                {
                    // 触摸释放，摇杆归位
                    _isTouching = false;
                    _touchX = 0f;
                    _touchY = 0f;
                    _directionX = 0f;
                    _directionY = 0f;

                    // 触发归位事件（方向为零）
                    JoystickMoved?.Invoke(this, new JoystickEventArgs { X = 0f, Y = 0f });

                    // 请求重绘
                    InvalidateSurface();
                    e.Handled = true;
                    break;
                }
        }
    }
}
