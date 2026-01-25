using DrawingSize = System.Drawing.Size;
using System.Collections.Generic;
using SharpGen.Runtime;
using Vortice.DCommon;
using Vortice.Direct2D1;
using Vortice.Direct3D11;
using Vortice.DirectWrite;
using Vortice.DXGI;
using Color4 = Vortice.Mathematics.Color4;
using Rect = Vortice.Mathematics.Rect;
using D2DFactoryType = Vortice.Direct2D1.FactoryType;
using DWriteFactoryType = Vortice.DirectWrite.FactoryType;
using DxgiAlphaMode = Vortice.DCommon.AlphaMode;

namespace MirClient.Rendering.D3D11;

public sealed class D3D11TextRenderer : IDisposable
{
    private readonly ID2D1Factory1 _d2dFactory;
    private readonly ID2D1Device _d2dDevice;
    private readonly ID2D1DeviceContext _d2dContext;
    private readonly IDWriteFactory _dwriteFactory;
    private readonly IDWriteTextFormat _defaultFormat;
    private readonly string _fontFamily;
    private readonly float _defaultFontSizeDip;
    private readonly Dictionary<TextFormatKey, IDWriteTextFormat> _formatCache = new();
    private readonly ID2D1SolidColorBrush _brush;

    private IDXGISurface? _dxgiSurface;
    private ID2D1Bitmap1? _targetBitmap;
    private DrawingSize _targetSize;
    private bool _drawing;

    private readonly record struct TextFormatKey(int SizeTimes100, FontWeight Weight);

    public D3D11TextRenderer(ID3D11Device d3dDevice, string fontFamily = "SimSun", float fontSizeDip = 12.0f)
    {
        ArgumentNullException.ThrowIfNull(d3dDevice);

        _d2dFactory = CreateD2DFactory();

        using IDXGIDevice dxgiDevice = d3dDevice.QueryInterface<IDXGIDevice>();
        _d2dDevice = _d2dFactory.CreateDevice(dxgiDevice);
        _d2dContext = _d2dDevice.CreateDeviceContext(DeviceContextOptions.None);

        _dwriteFactory = CreateDWriteFactory();
        _fontFamily = fontFamily;
        _defaultFontSizeDip = fontSizeDip;
        _defaultFormat = _dwriteFactory.CreateTextFormat(
            _fontFamily,
            fontCollection: null,
            fontWeight: FontWeight.Normal,
            fontStyle: Vortice.DirectWrite.FontStyle.Normal,
            fontStretch: FontStretch.Normal,
            fontSize: _defaultFontSizeDip,
            localeName: "zh-cn");

        _defaultFormat.TextAlignment = TextAlignment.Leading;
        _defaultFormat.ParagraphAlignment = ParagraphAlignment.Near;
        _defaultFormat.WordWrapping = WordWrapping.NoWrap;

        _brush = _d2dContext.CreateSolidColorBrush(new Color4(1, 1, 1, 1));
        _d2dContext.TextAntialiasMode = Vortice.Direct2D1.TextAntialiasMode.Grayscale;
    }

    public void Dispose()
    {
        End();

        _targetBitmap?.Dispose();
        _targetBitmap = null;
        _dxgiSurface?.Dispose();
        _dxgiSurface = null;

        _brush.Dispose();
        foreach (IDWriteTextFormat format in _formatCache.Values)
            format.Dispose();
        _formatCache.Clear();
        _defaultFormat.Dispose();
        _dwriteFactory.Dispose();
        _d2dContext.Dispose();
        _d2dDevice.Dispose();
        _d2dFactory.Dispose();
    }

    public void Begin(IDXGISwapChain1 swapChain, DrawingSize backBufferSize)
    {
        ArgumentNullException.ThrowIfNull(swapChain);

        if (_drawing)
            throw new InvalidOperationException("Begin called twice without End.");

        EnsureTargetBitmap(swapChain, backBufferSize);

        _d2dContext.BeginDraw();
        _drawing = true;
    }

    private IDWriteTextFormat GetOrCreateFormat(float fontSizeDip, FontWeight weight)
    {
        if (weight == FontWeight.Normal && Math.Abs(fontSizeDip - _defaultFontSizeDip) <= 0.01f)
            return _defaultFormat;

        int sizeTimes100 = (int)MathF.Round(MathF.Max(1f, fontSizeDip) * 100f);
        var key = new TextFormatKey(sizeTimes100, weight);
        if (_formatCache.TryGetValue(key, out IDWriteTextFormat? cached))
            return cached;

        float size = sizeTimes100 / 100f;
        IDWriteTextFormat created = _dwriteFactory.CreateTextFormat(
            _fontFamily,
            fontCollection: null,
            fontWeight: weight,
            fontStyle: Vortice.DirectWrite.FontStyle.Normal,
            fontStretch: FontStretch.Normal,
            fontSize: size,
            localeName: "zh-cn");

        created.TextAlignment = TextAlignment.Leading;
        created.ParagraphAlignment = ParagraphAlignment.Near;
        created.WordWrapping = WordWrapping.NoWrap;

        _formatCache.Add(key, created);
        return created;
    }

    public float MeasureTextWidth(string text)
    {
        if (string.IsNullOrEmpty(text))
            return 0f;

        return MeasureTextWidth(text, _defaultFontSizeDip, bold: false);
    }

    public (float Width, float Height) MeasureText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return (0f, 0f);

        return MeasureText(text, _defaultFontSizeDip, bold: false);
    }

    public void DrawText(string text, float x, float y, Color4 color)
    {
        DrawText(text, x, y, color, _defaultFontSizeDip, bold: false);
    }

    public float MeasureTextWidth(string text, float fontSizeDip, bool bold)
    {
        if (string.IsNullOrEmpty(text))
            return 0f;

        IDWriteTextFormat format = GetOrCreateFormat(fontSizeDip, bold ? FontWeight.Bold : FontWeight.Normal);
        using IDWriteTextLayout layout = _dwriteFactory.CreateTextLayout(text, format, float.PositiveInfinity, float.PositiveInfinity);
        return layout.Metrics.WidthIncludingTrailingWhitespace;
    }

    public (float Width, float Height) MeasureText(string text, float fontSizeDip, bool bold)
    {
        if (string.IsNullOrEmpty(text))
            return (0f, 0f);

        IDWriteTextFormat format = GetOrCreateFormat(fontSizeDip, bold ? FontWeight.Bold : FontWeight.Normal);
        using IDWriteTextLayout layout = _dwriteFactory.CreateTextLayout(text, format, float.PositiveInfinity, float.PositiveInfinity);

        TextMetrics metrics = layout.Metrics;
        return (metrics.WidthIncludingTrailingWhitespace, metrics.Height);
    }

    public void DrawText(string text, float x, float y, Color4 color, float fontSizeDip, bool bold)
    {
        if (!_drawing)
            throw new InvalidOperationException("DrawText must be called between Begin/End.");

        if (string.IsNullOrEmpty(text))
            return;

        _brush.Color = color;

        float right = Math.Max(x, _targetSize.Width);
        float bottom = Math.Max(y, _targetSize.Height);

        var layout = new Rect(x, y, right, bottom);

        IDWriteTextFormat format = GetOrCreateFormat(fontSizeDip, bold ? FontWeight.Bold : FontWeight.Normal);
        _d2dContext.DrawText(
            text,
            format,
            layout,
            _brush,
            DrawTextOptions.None,
            MeasuringMode.Natural);
    }

    public void End()
    {
        if (!_drawing)
            return;

        _drawing = false;

        Result hr = _d2dContext.EndDraw(out _, out _);
        if (hr.Failure)
        {
            const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);
            if (hr.Code == D2DERR_RECREATE_TARGET)
            {
                _targetBitmap?.Dispose();
                _targetBitmap = null;
                _dxgiSurface?.Dispose();
                _dxgiSurface = null;
                _targetSize = default;
            }
        }
    }

    private static ID2D1Factory1 CreateD2DFactory()
    {
        var options = new FactoryOptions
        {
#if DEBUG
            DebugLevel = DebugLevel.Information
#else
            DebugLevel = DebugLevel.None
#endif
        };

        Result hr = D2D1.D2D1CreateFactory(D2DFactoryType.SingleThreaded, options, out ID2D1Factory1? factory);
        if (hr.Failure)
            throw new InvalidOperationException($"D2D1CreateFactory failed: 0x{hr.Code:X8}");

        return factory ?? throw new InvalidOperationException("D2D1CreateFactory returned null factory.");
    }

    private static IDWriteFactory CreateDWriteFactory()
    {
        Result hr = DWrite.DWriteCreateFactory(DWriteFactoryType.Shared, out IDWriteFactory? factory);
        if (hr.Failure)
            throw new InvalidOperationException($"DWriteCreateFactory failed: 0x{hr.Code:X8}");

        return factory ?? throw new InvalidOperationException("DWriteCreateFactory returned null factory.");
    }

    private void EnsureTargetBitmap(IDXGISwapChain1 swapChain, DrawingSize backBufferSize)
    {
        if (_targetBitmap != null && _dxgiSurface != null && _targetSize == backBufferSize)
            return;

        _targetBitmap?.Dispose();
        _targetBitmap = null;
        _dxgiSurface?.Dispose();
        _dxgiSurface = null;
        _targetSize = backBufferSize;

        _dxgiSurface = swapChain.GetBuffer<IDXGISurface>(0);

        var bitmapProps = new BitmapProperties1(
            new PixelFormat(Format.B8G8R8A8_UNorm, DxgiAlphaMode.Premultiplied),
            dpiX: 96,
            dpiY: 96,
            bitmapOptions: BitmapOptions.Target | BitmapOptions.CannotDraw);

        _targetBitmap = _d2dContext.CreateBitmapFromDxgiSurface(_dxgiSurface, bitmapProps);
        _d2dContext.Target = _targetBitmap;
    }
}
