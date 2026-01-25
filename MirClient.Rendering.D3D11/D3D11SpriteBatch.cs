using DrawingRectangle = System.Drawing.Rectangle;
using DrawingSize = System.Drawing.Size;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using SharpGen.Runtime;
using Vortice.D3DCompiler;
using Vortice.Direct3D;
using Vortice.Direct3D11;
using Color4 = Vortice.Mathematics.Color4;

namespace MirClient.Rendering.D3D11;

public enum SpriteSampler
{
    Point,
    Linear
}

public enum SpriteBlendMode
{
    Opaque,
    AlphaBlend,
    Additive,
    Multiply,
    SourceColorAdd,
    BeBlend
}

public readonly struct SpriteBatchStats(int drawCalls, int textureBinds, int sprites, int scissorChanges)
{
    public int DrawCalls { get; } = drawCalls;
    public int TextureBinds { get; } = textureBinds;
    public int Sprites { get; } = sprites;
    public int ScissorChanges { get; } = scissorChanges;
}

public sealed class D3D11SpriteBatch : IDisposable
{
    private const int DefaultMaxSprites = 2048;

    private static readonly int VertexStride = Marshal.SizeOf<SpriteVertex>();

    private readonly ID3D11Device _device;

    private ID3D11VertexShader? _vertexShader;
    private ID3D11PixelShader? _pixelShader;
    private ID3D11InputLayout? _inputLayout;
    private ID3D11Buffer? _vertexBuffer;
    private ID3D11Buffer? _indexBuffer;
    private ID3D11Buffer? _constantsBuffer;
    private ID3D11SamplerState? _samplerPoint;
    private ID3D11SamplerState? _samplerLinear;
    private ID3D11BlendState? _blendAlpha;
    private ID3D11BlendState? _blendOpaque;
    private ID3D11BlendState? _blendAdditive;
    private ID3D11BlendState? _blendMultiply;
    private ID3D11BlendState? _blendSourceColorAdd;
    private ID3D11BlendState? _blendBeBlend;
    private ID3D11RasterizerState? _rasterScissor;
    private ID3D11RasterizerState? _rasterNoScissor;
    private ID3D11DepthStencilState? _depthDisabled;

    private readonly ID3D11Buffer[] _boundVertexBuffers = new ID3D11Buffer[1];
    private readonly uint[] _boundVertexStrides = new uint[1];
    private readonly uint[] _boundVertexOffsets = new uint[1];

    private SpriteVertex[] _vertices = Array.Empty<SpriteVertex>();
    private ushort[] _indices = Array.Empty<ushort>();
    private int _maxSprites;
    private bool _indexBufferDirty = true;

    private ID3D11DeviceContext? _context;
    private DrawingSize _viewportSize;
    private D3D11ViewTransform _viewTransform;
    private bool _hasViewTransform;
    private Vector2 _viewScale = Vector2.One;
    private Vector2 _viewOffset = Vector2.Zero;
    private DrawingRectangle? _baseScissorRect;
    private DrawingRectangle? _scissorRect;
    private SpriteSampler _sampler;
    private SpriteBlendMode _blendMode;
    private bool _begun;

    private D3D11Texture2D? _currentTexture;
    private int _spriteCount;

    private int _statDrawCalls;
    private int _statTextureBinds;
    private int _statSprites;
    private int _statScissorChanges;

    public D3D11SpriteBatch(ID3D11Device device, int maxSprites = DefaultMaxSprites)
    {
        _device = device;
        EnsureCapacity(Math.Max(1, maxSprites));
        CreateDeviceResources();
    }

    public SpriteBatchStats Stats => new(_statDrawCalls, _statTextureBinds, _statSprites, _statScissorChanges);

    public void Dispose()
    {
        _vertexShader?.Dispose();
        _vertexShader = null;

        _pixelShader?.Dispose();
        _pixelShader = null;

        _inputLayout?.Dispose();
        _inputLayout = null;

        _vertexBuffer?.Dispose();
        _vertexBuffer = null;

        _indexBuffer?.Dispose();
        _indexBuffer = null;

        _constantsBuffer?.Dispose();
        _constantsBuffer = null;

        _samplerPoint?.Dispose();
        _samplerPoint = null;

        _samplerLinear?.Dispose();
        _samplerLinear = null;

        _blendAlpha?.Dispose();
        _blendAlpha = null;

        _blendOpaque?.Dispose();
        _blendOpaque = null;

        _blendAdditive?.Dispose();
        _blendAdditive = null;

        _blendMultiply?.Dispose();
        _blendMultiply = null;

        _blendSourceColorAdd?.Dispose();
        _blendSourceColorAdd = null;

        _blendBeBlend?.Dispose();
        _blendBeBlend = null;

        _rasterScissor?.Dispose();
        _rasterScissor = null;

        _rasterNoScissor?.Dispose();
        _rasterNoScissor = null;

        _depthDisabled?.Dispose();
        _depthDisabled = null;
    }

    public void Begin(
        ID3D11DeviceContext context,
        DrawingSize viewportSize,
        SpriteSampler sampler = SpriteSampler.Point,
        SpriteBlendMode blendMode = SpriteBlendMode.AlphaBlend,
        DrawingRectangle? scissorRect = null)
    {
        if (_begun)
            throw new InvalidOperationException("SpriteBatch.Begin called twice without End.");

        _context = context;
        _viewportSize = viewportSize;
        _viewTransform = default;
        _hasViewTransform = false;
        _viewScale = Vector2.One;
        _viewOffset = Vector2.Zero;
        _baseScissorRect = null;
        _sampler = sampler;
        _blendMode = blendMode;
        _scissorRect = ResolveScissorRect(scissorRect);
        _spriteCount = 0;
        _currentTexture = null;
        _statDrawCalls = 0;
        _statTextureBinds = 0;
        _statSprites = 0;
        _statScissorChanges = 0;
        _begun = true;

        ApplyPipelineState();
    }

    public void Begin(
        ID3D11DeviceContext context,
        D3D11ViewTransform view,
        SpriteSampler sampler = SpriteSampler.Point,
        SpriteBlendMode blendMode = SpriteBlendMode.AlphaBlend,
        DrawingRectangle? scissorRect = null)
    {
        if (_begun)
            throw new InvalidOperationException("SpriteBatch.Begin called twice without End.");

        _context = context;
        _viewportSize = view.BackBufferSize;
        _viewTransform = view;
        _hasViewTransform = true;
        _viewScale = view.Scale;
        _viewOffset = view.Offset;
        _baseScissorRect = view.ViewportRect == new DrawingRectangle(0, 0, view.BackBufferSize.Width, view.BackBufferSize.Height)
            ? null
            : view.ViewportRect;

        _sampler = sampler;
        _blendMode = blendMode;
        _scissorRect = ResolveScissorRect(scissorRect);
        _spriteCount = 0;
        _currentTexture = null;
        _statDrawCalls = 0;
        _statTextureBinds = 0;
        _statSprites = 0;
        _statScissorChanges = 0;
        _begun = true;

        ApplyPipelineState();
    }

    public void End()
    {
        if (!_begun)
            return;

        Flush();

        _begun = false;
        _context = null;
        _currentTexture = null;
        _baseScissorRect = null;
        _scissorRect = null;
    }

    public void SetScissorRect(DrawingRectangle? scissorRect)
    {
        if (!_begun)
            throw new InvalidOperationException("SetScissorRect must be called between Begin/End.");

        DrawingRectangle? resolved = ResolveScissorRect(scissorRect);
        if (_scissorRect == resolved)
            return;

        Flush();
        _scissorRect = resolved;
        _statScissorChanges++;
        ApplyScissorState();
    }

    public void SetBlendMode(SpriteBlendMode blendMode)
    {
        if (!_begun)
            throw new InvalidOperationException("SetBlendMode must be called between Begin/End.");

        if (_blendMode == blendMode)
            return;

        Flush();
        _blendMode = blendMode;
        ApplyBlend();
    }

    public void Draw(
        D3D11Texture2D texture,
        DrawingRectangle destination,
        DrawingRectangle? source = null,
        Color4? color = null)
    {
        if (!_begun)
            throw new InvalidOperationException("Draw must be called between Begin/End.");

        if (_context == null)
            throw new InvalidOperationException("SpriteBatch is not initialized.");

        if (_currentTexture != texture)
        {
            Flush();
            _currentTexture = texture;
            _context.PSSetShaderResource(0, texture.ShaderResourceView);
            _statTextureBinds++;
        }

        if (_spriteCount >= _maxSprites)
        {
            Flush();
        }

        Color4 c = color ?? new Color4(1, 1, 1, 1);
        Vector4 cv = new(c.R, c.G, c.B, c.A);

        float left = destination.Left;
        float top = destination.Top;
        float right = destination.Right;
        float bottom = destination.Bottom;

        float u0, v0, u1, v1;
        if (source is { } s)
        {
            u0 = s.Left / (float)texture.Width;
            v0 = s.Top / (float)texture.Height;
            u1 = s.Right / (float)texture.Width;
            v1 = s.Bottom / (float)texture.Height;
        }
        else
        {
            u0 = 0;
            v0 = 0;
            u1 = 1;
            v1 = 1;
        }

        int baseVertex = _spriteCount * 4;
        _vertices[baseVertex + 0] = new SpriteVertex(new Vector2(left, top), new Vector2(u0, v0), cv);
        _vertices[baseVertex + 1] = new SpriteVertex(new Vector2(right, top), new Vector2(u1, v0), cv);
        _vertices[baseVertex + 2] = new SpriteVertex(new Vector2(right, bottom), new Vector2(u1, v1), cv);
        _vertices[baseVertex + 3] = new SpriteVertex(new Vector2(left, bottom), new Vector2(u0, v1), cv);

        _spriteCount++;
        _statSprites++;
    }

    private void ApplyPipelineState()
    {
        if (_context == null)
            return;

        _context.IASetPrimitiveTopology(PrimitiveTopology.TriangleList);
        _context.IASetInputLayout(_inputLayout!);

        _context.VSSetShader(_vertexShader!);
        _context.PSSetShader(_pixelShader!);

        _context.VSSetConstantBuffer(0, _constantsBuffer!);

        _boundVertexBuffers[0] = _vertexBuffer!;
        _boundVertexStrides[0] = (uint)VertexStride;
        _boundVertexOffsets[0] = 0;
        _context.IASetVertexBuffers(0, 1, _boundVertexBuffers, _boundVertexStrides, _boundVertexOffsets);

        EnsureIndexBufferFilled();
        _context.IASetIndexBuffer(_indexBuffer!, Vortice.DXGI.Format.R16_UInt, 0u);

        ApplyConstants();
        ApplySampler();
        ApplyBlend();
        ApplyScissorState();
        ApplyDepth();
    }

    private void ApplyConstants()
    {
        if (_context == null || _constantsBuffer == null)
            return;

        var constants = new SpriteConstants
        {
            BackBufferSize = new Vector2(
                Math.Max(1, _viewportSize.Width),
                Math.Max(1, _viewportSize.Height)),
            ViewScale = _viewScale,
            ViewOffset = _viewOffset
        };

        unsafe
        {
            MappedSubresource mapped = _context.Map(_constantsBuffer, MapMode.WriteDiscard, MapFlags.None);
            *(SpriteConstants*)mapped.DataPointer = constants;
            _context.Unmap(_constantsBuffer, 0);
        }
    }

    private void ApplySampler()
    {
        if (_context == null)
            return;

        ID3D11SamplerState sampler = _sampler == SpriteSampler.Linear ? _samplerLinear! : _samplerPoint!;
        _context.PSSetSampler(0, sampler);
    }

    private void ApplyBlend()
    {
        if (_context == null)
            return;

        ID3D11BlendState blend = _blendMode switch
        {
            SpriteBlendMode.Opaque => _blendOpaque!,
            SpriteBlendMode.AlphaBlend => _blendAlpha!,
            SpriteBlendMode.Additive => _blendAdditive!,
            SpriteBlendMode.Multiply => _blendMultiply!,
            SpriteBlendMode.SourceColorAdd => _blendSourceColorAdd!,
            SpriteBlendMode.BeBlend => _blendBeBlend!,
            _ => _blendAlpha!
        };
        _context.OMSetBlendState(blend, new Color4(0, 0, 0, 0), uint.MaxValue);
    }

    private void ApplyDepth()
    {
        if (_context == null)
            return;

        _context.OMSetDepthStencilState(_depthDisabled!, 0);
    }

    private void ApplyScissorState()
    {
        if (_context == null)
            return;

        if (_scissorRect is { } scissor)
        {
            _context.RSSetState(_rasterScissor!);
            _context.RSSetScissorRect(scissor.Left, scissor.Top, scissor.Right, scissor.Bottom);
        }
        else
        {
            _context.RSSetState(_rasterNoScissor!);
        }
    }

    private void Flush()
    {
        if (!_begun || _context == null || _spriteCount <= 0)
            return;

        int vertexCount = _spriteCount * 4;
        int indexCount = _spriteCount * 6;

        unsafe
        {
            MappedSubresource mapped = _context.Map(_vertexBuffer!, MapMode.WriteDiscard, MapFlags.None);
            fixed (SpriteVertex* pSrc = _vertices)
            {
                Buffer.MemoryCopy(
                    source: pSrc,
                    destination: (void*)mapped.DataPointer,
                    destinationSizeInBytes: (long)_maxSprites * 4 * VertexStride,
                    sourceBytesToCopy: (long)vertexCount * VertexStride);
            }
            _context.Unmap(_vertexBuffer!, 0);
        }

        _context.DrawIndexed((uint)indexCount, 0, 0);
        _statDrawCalls++;
        _spriteCount = 0;
    }

    private void EnsureCapacity(int minSprites)
    {
        if (minSprites <= _maxSprites)
            return;

        int newMax = _maxSprites <= 0 ? 1 : _maxSprites;
        while (newMax < minSprites)
            newMax *= 2;

        _maxSprites = newMax;
        _vertices = new SpriteVertex[_maxSprites * 4];
        _indices = new ushort[_maxSprites * 6];

        for (int i = 0; i < _maxSprites; i++)
        {
            int baseVertex = i * 4;
            int baseIndex = i * 6;
            _indices[baseIndex + 0] = (ushort)(baseVertex + 0);
            _indices[baseIndex + 1] = (ushort)(baseVertex + 1);
            _indices[baseIndex + 2] = (ushort)(baseVertex + 2);
            _indices[baseIndex + 3] = (ushort)(baseVertex + 0);
            _indices[baseIndex + 4] = (ushort)(baseVertex + 2);
            _indices[baseIndex + 5] = (ushort)(baseVertex + 3);
        }

        _vertexBuffer?.Dispose();
        _vertexBuffer = null;
        _indexBuffer?.Dispose();
        _indexBuffer = null;

        if (_vertexShader != null)
        {
            CreateBuffers();
        }
    }

    private void CreateDeviceResources()
    {
        CreateShaders();
        CreateBuffers();
        CreateStates();
    }

    private void CreateShaders()
    {
        const string shaderSource = """
cbuffer SpriteConstants : register(b0)
{
    float2 BackBufferSize;
    float2 ViewScale;
    float2 ViewOffset;
    float2 _pad0;
};

struct VSIn
{
    float2 Pos   : POSITION;
    float2 Tex   : TEXCOORD0;
    float4 Color : COLOR0;
};

struct PSIn
{
    float4 Pos   : SV_POSITION;
    float2 Tex   : TEXCOORD0;
    float4 Color : COLOR0;
};

PSIn VSMain(VSIn input)
{
    PSIn o;
    float2 screen = input.Pos * ViewScale + ViewOffset;
    float2 ndc;
    ndc.x = (screen.x / BackBufferSize.x) * 2.0 - 1.0;
    ndc.y = 1.0 - (screen.y / BackBufferSize.y) * 2.0;
    o.Pos = float4(ndc, 0.0, 1.0);
    o.Tex = input.Tex;
    o.Color = input.Color;
    return o;
}

Texture2D SpriteTex : register(t0);
SamplerState SpriteSampler : register(s0);

float4 PSMain(PSIn input) : SV_Target
{
    float4 tex = SpriteTex.Sample(SpriteSampler, input.Tex);

    if (input.Color.r < 0.0)
    {
        float gray = dot(tex.rgb, float3(0.299, 0.587, 0.114));
        tex.rgb = float3(gray, gray, gray);
        return tex * float4(-input.Color.rgb, input.Color.a);
    }

    return tex * input.Color;
}
""";

        using var vsBlob = ShaderCompiler.Compile(shaderSource, "VSMain", "vs_4_0");
        using var psBlob = ShaderCompiler.Compile(shaderSource, "PSMain", "ps_4_0");

        unsafe
        {
            _vertexShader = _device.CreateVertexShader((void*)vsBlob.BufferPointer, vsBlob.BufferSize, null);
            _pixelShader = _device.CreatePixelShader((void*)psBlob.BufferPointer, psBlob.BufferSize, null);
        }

        InputElementDescription[] elements =
        [
            new InputElementDescription("POSITION", 0, Vortice.DXGI.Format.R32G32_Float, 0, 0),
            new InputElementDescription("TEXCOORD", 0, Vortice.DXGI.Format.R32G32_Float, 8, 0),
            new InputElementDescription("COLOR", 0, Vortice.DXGI.Format.R32G32B32A32_Float, 16, 0)
        ];

        unsafe
        {
            _inputLayout = _device.CreateInputLayout(
                elements,
                new Span<byte>((void*)vsBlob.BufferPointer, checked((int)(nuint)vsBlob.BufferSize)));
        }
    }

    private void CreateBuffers()
    {
        _vertexBuffer?.Dispose();
        _vertexBuffer = null;
        _indexBuffer?.Dispose();
        _indexBuffer = null;
        _indexBufferDirty = true;
        _constantsBuffer?.Dispose();
        _constantsBuffer = null;

        var vbDesc = new BufferDescription
        {
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.VertexBuffer,
            CPUAccessFlags = CpuAccessFlags.Write,
            MiscFlags = ResourceOptionFlags.None,
            ByteWidth = checked((uint)(_maxSprites * 4 * VertexStride)),
            StructureByteStride = 0u
        };

        _vertexBuffer = _device.CreateBuffer(vbDesc);

        var ibDesc = new BufferDescription
        {
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.IndexBuffer,
            CPUAccessFlags = CpuAccessFlags.Write,
            MiscFlags = ResourceOptionFlags.None,
            ByteWidth = checked((uint)(_maxSprites * 6 * sizeof(ushort))),
            StructureByteStride = 0u
        };

        _indexBuffer = _device.CreateBuffer(ibDesc);

        uint cbSize = (uint)((Marshal.SizeOf<SpriteConstants>() + 15) & ~15);
        var cbDesc = new BufferDescription
        {
            Usage = ResourceUsage.Dynamic,
            BindFlags = BindFlags.ConstantBuffer,
            CPUAccessFlags = CpuAccessFlags.Write,
            MiscFlags = ResourceOptionFlags.None,
            ByteWidth = cbSize,
            StructureByteStride = 0u
        };
        _constantsBuffer = _device.CreateBuffer(cbDesc);
    }

    private void CreateStates()
    {
        var samplerBorderColor = new Color4(0, 0, 0, 0);

        _samplerPoint = _device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipPoint,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            MipLODBias = 0,
            MaxAnisotropy = 1u,
            ComparisonFunc = ComparisonFunction.Never,
            BorderColor = samplerBorderColor,
            MinLOD = 0,
            MaxLOD = float.MaxValue
        });

        _samplerLinear = _device.CreateSamplerState(new SamplerDescription
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            MipLODBias = 0,
            MaxAnisotropy = 1u,
            ComparisonFunc = ComparisonFunction.Never,
            BorderColor = samplerBorderColor,
            MinLOD = 0,
            MaxLOD = float.MaxValue
        });

        _blendOpaque = _device.CreateBlendState(BlendDescription.Opaque);
        _blendAlpha = _device.CreateBlendState(BlendDescription.NonPremultiplied);
        _blendAdditive = _device.CreateBlendState(BlendDescription.Additive);
        _blendMultiply = _device.CreateBlendState(CreateMultiplyBlendDescription());
        _blendSourceColorAdd = _device.CreateBlendState(CreateSourceColorAddBlendDescription());
        _blendBeBlend = _device.CreateBlendState(CreateBeBlendBlendDescription());

        _rasterNoScissor = _device.CreateRasterizerState(new RasterizerDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            FrontCounterClockwise = false,
            DepthClipEnable = true,
            ScissorEnable = false
        });

        _rasterScissor = _device.CreateRasterizerState(new RasterizerDescription
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            FrontCounterClockwise = false,
            DepthClipEnable = true,
            ScissorEnable = true
        });

        _depthDisabled = _device.CreateDepthStencilState(new DepthStencilDescription
        {
            DepthEnable = false,
            DepthWriteMask = DepthWriteMask.Zero,
            DepthFunc = ComparisonFunction.Always,
            StencilEnable = false
        });
    }

    private void EnsureIndexBufferFilled()
    {
        if (_context == null || _indexBuffer == null || !_indexBufferDirty)
            return;

        unsafe
        {
            MappedSubresource mapped = _context.Map(_indexBuffer, MapMode.WriteDiscard, MapFlags.None);
            fixed (ushort* pSrc = _indices)
            {
                long bytes = (long)_indices.Length * sizeof(ushort);
                Buffer.MemoryCopy(pSrc, (void*)mapped.DataPointer, bytes, bytes);
            }
            _context.Unmap(_indexBuffer, 0);
        }

        _indexBufferDirty = false;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpriteVertex(Vector2 position, Vector2 texCoord, Vector4 color)
    {
        public Vector2 Position = position;
        public Vector2 TexCoord = texCoord;
        public Vector4 Color = color;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SpriteConstants
    {
        public Vector2 BackBufferSize;
        public Vector2 ViewScale;
        public Vector2 ViewOffset;
        public Vector2 Padding0;
    }

    private static class ShaderCompiler
    {
        public static Blob Compile(string source, string entryPoint, string profile)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(source);

            unsafe
            {
                fixed (byte* p = bytes)
                {
                    ShaderFlags flags = ShaderFlags.EnableStrictness | ShaderFlags.OptimizationLevel3;
#if DEBUG
                    flags |= ShaderFlags.Debug;
#endif

                    Result hr = Compiler.Compile(
                        p,
                        new PointerUSize((nuint)bytes.Length),
                        null,
                        null,
                        null,
                        entryPoint,
                        profile,
                        flags,
                        EffectFlags.None,
                        out Blob code,
                        out Blob error);

                    if (hr.Failure)
                    {
                        string details = ReadBlobAsString(error) ?? $"0x{hr.Code:X8}";
                        error?.Dispose();
                        code?.Dispose();
                        throw new InvalidOperationException($"{entryPoint}/{profile} compile failed: {details}");
                    }

                    error?.Dispose();
                    return code;
                }
            }
        }

        private static string? ReadBlobAsString(Blob? blob)
        {
            if (blob == null || blob.BufferPointer == IntPtr.Zero || blob.BufferSize == 0)
                return null;

            int size = (int)Math.Min(int.MaxValue, (nuint)blob.BufferSize);
            if (size <= 0)
                return null;

            string? text = Marshal.PtrToStringAnsi(blob.BufferPointer, size);
            return text?.TrimEnd('\0');
        }
    }

    private DrawingRectangle? ResolveScissorRect(DrawingRectangle? scissorRect)
    {
        DrawingRectangle? rect = scissorRect;

        if (_hasViewTransform && rect is { } r)
            rect = _viewTransform.ToBackBuffer(r);

        if (_baseScissorRect is { } baseRect)
            rect = rect is { } rr ? DrawingRectangle.Intersect(baseRect, rr) : baseRect;

        return rect;
    }

    private static BlendDescription CreateMultiplyBlendDescription()
    {
        var desc = new BlendDescription
        {
            AlphaToCoverageEnable = false,
            IndependentBlendEnable = false
        };

        desc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.DestinationColor,
            DestinationBlend = Blend.Zero,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.Zero,
            DestinationBlendAlpha = Blend.One,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All
        };

        return desc;
    }

    private static BlendDescription CreateSourceColorAddBlendDescription()
    {
        var desc = new BlendDescription
        {
            AlphaToCoverageEnable = false,
            IndependentBlendEnable = false
        };

        desc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.SourceColor,
            DestinationBlend = Blend.One,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.Zero,
            DestinationBlendAlpha = Blend.One,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All
        };

        return desc;
    }

    private static BlendDescription CreateBeBlendBlendDescription()
    {
        var desc = new BlendDescription
        {
            AlphaToCoverageEnable = false,
            IndependentBlendEnable = false
        };

        desc.RenderTarget[0] = new RenderTargetBlendDescription
        {
            BlendEnable = true,
            SourceBlend = Blend.SourceAlpha,
            DestinationBlend = Blend.InverseSourceColor,
            BlendOperation = BlendOperation.Add,
            SourceBlendAlpha = Blend.SourceAlpha,
            DestinationBlendAlpha = Blend.InverseSourceAlpha,
            BlendOperationAlpha = BlendOperation.Add,
            RenderTargetWriteMask = ColorWriteEnable.All
        };

        return desc;
    }

}
