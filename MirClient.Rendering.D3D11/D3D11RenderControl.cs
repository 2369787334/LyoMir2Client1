using System.ComponentModel;
using System.Windows.Forms;
using SharpGen.Runtime;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;
using static Vortice.Direct3D11.D3D11;

namespace MirClient.Rendering.D3D11;

public sealed class D3D11RenderControl : UserControl
{
    private readonly object _gate = new();

    private ID3D11Device? _device;
    private ID3D11DeviceContext? _context;
    private IDXGISwapChain1? _swapChain;
    private ID3D11RenderTargetView? _renderTargetView;

    public int DeviceVersion { get; private set; }

    public D3D11RenderControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.Opaque | ControlStyles.UserPaint, true);
        UpdateStyles();
    }

    [Browsable(false)]
    public bool IsInitialized => _device != null && _context != null && _swapChain != null && _renderTargetView != null;

    [DefaultValue(true)]
    public bool VSync { get; set; } = true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        if (DesignMode)
            return;

        InitializeDeviceResources();
    }

    protected override void OnHandleDestroyed(EventArgs e)
    {
        if (!DesignMode)
            DisposeDeviceResources();

        base.OnHandleDestroyed(e);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);

        if (!IsInitialized)
            return;

        ResizeSwapChain();
    }

    public void Render(Color4 clearColor)
    {
        Render(clearColor, static _ => { });
    }

    public void Render(Color4 clearColor, Action<D3D11Frame> draw)
    {
        ArgumentNullException.ThrowIfNull(draw);

        lock (_gate)
        {
            if (!IsInitialized)
                return;

            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;

            _context!.OMSetRenderTargets(_renderTargetView!, null);
            _context.RSSetViewport(new Viewport(0, 0, ClientSize.Width, ClientSize.Height));
            _context.ClearRenderTargetView(_renderTargetView!, clearColor);

            draw(new D3D11Frame(_device!, _context!, _renderTargetView!, _swapChain!, ClientSize, DeviceVersion));

            Result result = _swapChain!.Present(VSync ? 1u : 0u, PresentFlags.None);
            if (result.Failure && IsDeviceLost(result))
            {
                HandleDeviceLost();
            }
        }
    }

    private void InitializeDeviceResources()
    {
        lock (_gate)
        {
            if (_device != null)
                return;

            DeviceCreationFlags flags = DeviceCreationFlags.BgraSupport;
#if DEBUG
            flags |= DeviceCreationFlags.Debug;
#endif

            FeatureLevel[] featureLevels =
            [
                FeatureLevel.Level_11_1,
                FeatureLevel.Level_11_0,
                FeatureLevel.Level_10_1,
                FeatureLevel.Level_10_0
            ];

            Result result = D3D11CreateDevice(null, DriverType.Hardware, flags, featureLevels, out _device, out _context);

            if (result.Failure)
                throw new InvalidOperationException($"D3D11CreateDevice failed: 0x{result.Code:X8}");

            DeviceVersion++;

            using IDXGIDevice dxgiDevice = _device.QueryInterface<IDXGIDevice>();
            using IDXGIAdapter adapter = dxgiDevice.GetAdapter();
            using IDXGIFactory2 factory = adapter.GetParent<IDXGIFactory2>();

            var swapChainDesc = new SwapChainDescription1
            {
                Width = (uint)Math.Max(1, ClientSize.Width),
                Height = (uint)Math.Max(1, ClientSize.Height),
                Format = Format.B8G8R8A8_UNorm,
                Stereo = false,
                SampleDescription = new SampleDescription(1, 0),
                BufferUsage = Usage.RenderTargetOutput,
                BufferCount = 2,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipDiscard,
                AlphaMode = AlphaMode.Ignore,
                Flags = SwapChainFlags.None
            };

            _swapChain = factory.CreateSwapChainForHwnd(_device, Handle, swapChainDesc);
            CreateRenderTargetView();
        }
    }

    private void ResizeSwapChain()
    {
        lock (_gate)
        {
            if (_swapChain == null)
                return;

            if (ClientSize.Width <= 0 || ClientSize.Height <= 0)
                return;

            _context?.OMSetRenderTargets(Array.Empty<ID3D11RenderTargetView>(), null);

            _renderTargetView?.Dispose();
            _renderTargetView = null;

            Result result = _swapChain.ResizeBuffers(
                bufferCount: 0,
                width: (uint)Math.Max(1, ClientSize.Width),
                height: (uint)Math.Max(1, ClientSize.Height),
                newFormat: Format.Unknown,
                swapChainFlags: SwapChainFlags.None);

            if (result.Failure && IsDeviceLost(result))
            {
                HandleDeviceLost();
                return;
            }

            CreateRenderTargetView();
        }
    }

    private void CreateRenderTargetView()
    {
        if (_device == null || _swapChain == null)
            return;

        using ID3D11Texture2D backBuffer = _swapChain.GetBuffer<ID3D11Texture2D>(0);
        _renderTargetView = _device.CreateRenderTargetView(backBuffer);
    }

    private void DisposeDeviceResources()
    {
        lock (_gate)
        {
            _renderTargetView?.Dispose();
            _renderTargetView = null;

            _swapChain?.Dispose();
            _swapChain = null;

            _context?.Dispose();
            _context = null;

            _device?.Dispose();
            _device = null;
        }
    }

    private void HandleDeviceLost()
    {
        DisposeDeviceResources();
        InitializeDeviceResources();
    }

    private static bool IsDeviceLost(Result result) =>
        result.Code == unchecked((int)0x887A0005) || 
        result.Code == unchecked((int)0x887A0007); 
}
