using System.Drawing;
using System.Drawing.Imaging;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace MirClient.Rendering.D3D11;

public sealed class D3D11Texture2D : IDisposable
{
    public D3D11Texture2D(ID3D11Texture2D texture, ID3D11ShaderResourceView shaderResourceView, int width, int height)
        : this(texture, shaderResourceView, renderTargetView: null, width, height)
    {
    }

    public D3D11Texture2D(ID3D11Texture2D texture, ID3D11ShaderResourceView shaderResourceView, ID3D11RenderTargetView? renderTargetView, int width, int height)
    {
        Texture = texture;
        ShaderResourceView = shaderResourceView;
        RenderTargetView = renderTargetView;
        Width = width;
        Height = height;
    }

    public ID3D11Texture2D Texture { get; }
    public ID3D11ShaderResourceView ShaderResourceView { get; }
    public ID3D11RenderTargetView? RenderTargetView { get; }
    public int Width { get; }
    public int Height { get; }

    public void Dispose()
    {
        RenderTargetView?.Dispose();
        ShaderResourceView.Dispose();
        Texture.Dispose();
    }

    public static D3D11Texture2D CreateFromBgra32(ID3D11Device device, ReadOnlySpan<byte> bgra32, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Texture size must be positive.");

        int expectedBytes = checked(width * height * 4);
        if (bgra32.Length != expectedBytes)
            throw new ArgumentException($"Expected {expectedBytes} bytes for BGRA32 texture data, got {bgra32.Length}.", nameof(bgra32));

        var desc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Immutable,
            BindFlags = BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };

        unsafe
        {
            fixed (byte* p = bgra32)
            {
                var init = new SubresourceData((nint)p, (uint)(width * 4), 0);
                using ID3D11Texture2D texture = device.CreateTexture2D(desc, init);
                ID3D11ShaderResourceView srv = device.CreateShaderResourceView(texture);
                return new D3D11Texture2D(texture, srv, width, height);
            }
        }
    }

    public static D3D11Texture2D CreateWhite(ID3D11Device device)
    {
        Span<byte> bgra = stackalloc byte[4] { 255, 255, 255, 255 };
        return CreateFromBgra32(device, bgra, width: 1, height: 1);
    }

    public static D3D11Texture2D CreateCheckerboard(ID3D11Device device, int width, int height, int cellSize, Color colorA, Color colorB)
    {
        if (cellSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSize));

        byte[] bgra = new byte[checked(width * height * 4)];

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                bool a = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                Color c = a ? colorA : colorB;
                int offset = ((y * width) + x) * 4;
                bgra[offset + 0] = c.B;
                bgra[offset + 1] = c.G;
                bgra[offset + 2] = c.R;
                bgra[offset + 3] = c.A;
            }
        }

        return CreateFromBgra32(device, bgra, width, height);
    }

    public static D3D11Texture2D LoadFromFile(ID3D11Device device, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Path is required.", nameof(path));

        using var src = new Bitmap(path);
        using var bmp = EnsureFormat32bppArgb(src);

        var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
        BitmapData data = bmp.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            int width = bmp.Width;
            int height = bmp.Height;
            byte[] bgra = new byte[checked(width * height * 4)];

            unsafe
            {
                byte* pSrc = (byte*)data.Scan0;
                int srcStride = data.Stride;

                fixed (byte* pDst = bgra)
                {
                    for (int y = 0; y < height; y++)
                    {
                        Buffer.MemoryCopy(
                            source: pSrc + (y * srcStride),
                            destination: pDst + (y * width * 4),
                            destinationSizeInBytes: width * 4,
                            sourceBytesToCopy: width * 4);
                    }
                }
            }

            return CreateFromBgra32(device, bgra, width, height);
        }
        finally
        {
            bmp.UnlockBits(data);
        }
    }

    private static Bitmap EnsureFormat32bppArgb(Bitmap source)
    {
        if (source.PixelFormat == PixelFormat.Format32bppArgb)
            return (Bitmap)source.Clone();

        var target = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(target);
        g.DrawImage(source, 0, 0, source.Width, source.Height);
        return target;
    }

    public static D3D11Texture2D CreateRenderTarget(ID3D11Device device, int width, int height)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Texture size must be positive.");

        var desc = new Texture2DDescription
        {
            Width = (uint)width,
            Height = (uint)height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.RenderTarget | BindFlags.ShaderResource,
            CPUAccessFlags = CpuAccessFlags.None,
            MiscFlags = ResourceOptionFlags.None
        };

        ID3D11Texture2D texture = device.CreateTexture2D(desc);
        ID3D11RenderTargetView rtv = device.CreateRenderTargetView(texture);
        ID3D11ShaderResourceView srv = device.CreateShaderResourceView(texture);

        return new D3D11Texture2D(texture, srv, rtv, width, height);
    }
}
