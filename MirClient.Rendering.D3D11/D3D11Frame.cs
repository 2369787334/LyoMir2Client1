using System.Drawing;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace MirClient.Rendering.D3D11;

public readonly struct D3D11Frame(
    ID3D11Device device,
    ID3D11DeviceContext context,
    ID3D11RenderTargetView renderTargetView,
    IDXGISwapChain1 swapChain,
    Size backBufferSize,
    int deviceVersion)
{
    public ID3D11Device Device { get; } = device;
    public ID3D11DeviceContext Context { get; } = context;
    public ID3D11RenderTargetView RenderTargetView { get; } = renderTargetView;
    public IDXGISwapChain1 SwapChain { get; } = swapChain;
    public Size BackBufferSize { get; } = backBufferSize;
    public int DeviceVersion { get; } = deviceVersion;
}
