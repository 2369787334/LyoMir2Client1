using System.Drawing;
using System.Numerics;

namespace MirClient.Rendering.D3D11;

public enum D3D11ScaleMode
{
    None,
    Stretch,
    Fit,
    IntegerFit
}

public readonly struct D3D11ViewTransform(
    Size backBufferSize,
    Size logicalSize,
    D3D11ScaleMode scaleMode,
    Vector2 scale,
    Vector2 offset,
    Rectangle viewportRect)
{
    public Size BackBufferSize { get; } = backBufferSize;
    public Size LogicalSize { get; } = logicalSize;
    public D3D11ScaleMode ScaleMode { get; } = scaleMode;

    public Vector2 Scale { get; } = scale;
    public Vector2 Offset { get; } = offset;
    public Rectangle ViewportRect { get; } = viewportRect;

    public static D3D11ViewTransform Create(Size backBufferSize, Size logicalSize, D3D11ScaleMode mode)
    {
        if (backBufferSize.Width <= 0 || backBufferSize.Height <= 0)
            throw new ArgumentOutOfRangeException(nameof(backBufferSize));

        if (logicalSize.Width <= 0 || logicalSize.Height <= 0)
            logicalSize = backBufferSize;

        float backW = backBufferSize.Width;
        float backH = backBufferSize.Height;
        float logicalW = logicalSize.Width;
        float logicalH = logicalSize.Height;

        if (mode == D3D11ScaleMode.None)
        {
            return new D3D11ViewTransform(
                backBufferSize,
                logicalSize,
                mode,
                scale: new Vector2(1, 1),
                offset: Vector2.Zero,
                viewportRect: new Rectangle(0, 0, backBufferSize.Width, backBufferSize.Height));
        }

        if (mode == D3D11ScaleMode.Stretch)
        {
            return new D3D11ViewTransform(
                backBufferSize,
                logicalSize,
                mode,
                scale: new Vector2(backW / logicalW, backH / logicalH),
                offset: Vector2.Zero,
                viewportRect: new Rectangle(0, 0, backBufferSize.Width, backBufferSize.Height));
        }

        float s = MathF.Min(backW / logicalW, backH / logicalH);
        if (mode == D3D11ScaleMode.IntegerFit)
        {
            if (s >= 1.0f)
                s = MathF.Max(1.0f, MathF.Floor(s));
        }

        Vector2 scale = new(s, s);

        float vpW = logicalW * s;
        float vpH = logicalH * s;
        int vpX = (int)MathF.Round((backW - vpW) * 0.5f);
        int vpY = (int)MathF.Round((backH - vpH) * 0.5f);
        int vpWi = (int)MathF.Round(vpW);
        int vpHi = (int)MathF.Round(vpH);

        var viewportRect = new Rectangle(vpX, vpY, vpWi, vpHi);

        return new D3D11ViewTransform(
            backBufferSize,
            logicalSize,
            mode,
            scale,
            offset: new Vector2(viewportRect.X, viewportRect.Y),
            viewportRect);
    }

    public Vector2 ToBackBuffer(Vector2 logicalPosition) => (logicalPosition * Scale) + Offset;

    public Rectangle ToBackBuffer(Rectangle logicalRectangle)
    {
        Vector2 tl = ToBackBuffer(new Vector2(logicalRectangle.Left, logicalRectangle.Top));
        Vector2 br = ToBackBuffer(new Vector2(logicalRectangle.Right, logicalRectangle.Bottom));

        return Rectangle.FromLTRB(
            left: (int)MathF.Round(tl.X),
            top: (int)MathF.Round(tl.Y),
            right: (int)MathF.Round(br.X),
            bottom: (int)MathF.Round(br.Y));
    }
}

