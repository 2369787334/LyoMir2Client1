using System.Buffers.Binary;
using System.IO.Compression;
using MirClient.Assets.Palettes;
using MirClient.Assets.Wil;

namespace MirClient.Assets.Wzl;

public sealed class WzlFile : IDisposable
{
    private const int WzlHeaderSizeBytes = 64; 
    private const int WzxHeaderSizeBytes = 48; 
    private const int ImageInfoSizeBytes = 16; 

    private readonly FileStream _wzlStream;
    private readonly int[] _index;

    private WzlFile(string wzlPath, string? wzxPath, FileStream wzlStream, int[] index)
    {
        WzlPath = wzlPath;
        WzxPath = wzxPath;
        _wzlStream = wzlStream;
        _index = index;
    }

    public string WzlPath { get; }
    public string? WzxPath { get; }
    public int ImageCount => _index.Length;

    public static WzlFile Open(string wzlPath, string? wzxPath = null)
    {
        if (string.IsNullOrWhiteSpace(wzlPath))
            throw new ArgumentException("WZL path is required.", nameof(wzlPath));

        wzlPath = Path.GetFullPath(wzlPath);
        wzxPath ??= Path.ChangeExtension(wzlPath, ".wzx");
        wzxPath = string.IsNullOrWhiteSpace(wzxPath) ? null : Path.GetFullPath(wzxPath);

        var wzlStream = new FileStream(wzlPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            int imageCount = ReadImageCount(wzlStream);
            int[] index = wzxPath != null && File.Exists(wzxPath)
                ? ReadIndex(wzxPath, imageCount)
                : ScanSequentialIndex(wzlStream, imageCount);

            return new WzlFile(wzlPath, wzxPath, wzlStream, index);
        }
        catch
        {
            wzlStream.Dispose();
            throw;
        }
    }

    public void Dispose() => _wzlStream.Dispose();

    public bool HasImage(int imageIndex)
    {
        if ((uint)imageIndex >= (uint)_index.Length)
            return false;

        int position = _index[imageIndex];
        return position > 0 && position < _wzlStream.Length;
    }

    public bool TryDecodeImage(int imageIndex, out WilImage image)
    {
        image = null!;

        if ((uint)imageIndex >= (uint)_index.Length)
            return false;

        int position = _index[imageIndex];
        if (position <= 0)
            return false;

        try
        {
            if (position + ImageInfoSizeBytes > _wzlStream.Length)
                return false;

            _wzlStream.Position = position;

            if (!TryReadImageInfo(_wzlStream, out WzlImageInfo info))
                return false;

            int width = info.Width;
            int height = info.Height;
            if (width <= 0 || height <= 0)
                return false;

            int bitCount = ResolveBitCount(info.Enc1);
            if (bitCount == 0)
                return false;

            byte[] raw;

            if (info.CompressedLength > 0)
            {
                long dataEnd = _wzlStream.Position + info.CompressedLength;
                if (dataEnd > _wzlStream.Length)
                    return false;

                byte[] compressed = new byte[info.CompressedLength];
                _wzlStream.ReadExactly(compressed);
                raw = ZlibDecompress(compressed);
            }
            else
            {
                int bytesPerPixel = bitCount / 8;
                int expected = checked(width * height * bytesPerPixel);
                long dataEnd = _wzlStream.Position + expected;
                if (dataEnd > _wzlStream.Length)
                    return false;

                raw = new byte[expected];
                _wzlStream.ReadExactly(raw);
            }

            byte[] bgra;
            if (info.Enc2 == 9)
            {
                if (!TryDecodeR5G6B5Alpha(raw, width, height, out bgra))
                    return false;
            }
            else
            {
                if (!TryDecodeToBgra32(raw, width, height, bitCount, out bgra))
                    return false;
            }

            image = new WilImage(width, height, info.Px, info.Py, bgra);
            return true;
        }
        catch
        {
            image = null!;
            return false;
        }
    }

    private static int ReadImageCount(Stream stream)
    {
        if (stream.Length < WzlHeaderSizeBytes)
            throw new InvalidDataException("WZL header too short.");

        stream.Position = 0;
        Span<byte> header = stackalloc byte[WzlHeaderSizeBytes];
        stream.ReadExactly(header);

        int indexCount = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(44, 4));
        if (indexCount < 0 || indexCount > 10_000_000)
            throw new InvalidDataException($"Invalid WZL index count: {indexCount}.");

        return indexCount;
    }

    private static int[] ReadIndex(string wzxPath, int imageCount)
    {
        if (imageCount <= 0)
            return Array.Empty<int>();

        using var stream = new FileStream(wzxPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length < WzxHeaderSizeBytes)
            return new int[imageCount];

        stream.Position = 0;
        Span<byte> header = stackalloc byte[WzxHeaderSizeBytes];
        stream.ReadExactly(header);

        long remaining = stream.Length - stream.Position;
        if (remaining <= 0)
            return new int[imageCount];

        if (remaining > int.MaxValue)
            return new int[imageCount];

        byte[] buf = new byte[(int)remaining];
        stream.ReadExactly(buf);

        var index = new int[imageCount];
        for (int i = 0; i < index.Length; i++)
        {
            int o = i * 4;
            index[i] = o + 4 <= buf.Length ? BinaryPrimitives.ReadInt32LittleEndian(buf.AsSpan(o, 4)) : 0;
        }

        return index;
    }

    private static int[] ScanSequentialIndex(FileStream wzlStream, int imageCount)
    {
        if (imageCount <= 0)
            return Array.Empty<int>();

        if (wzlStream.Length < WzlHeaderSizeBytes)
            return new int[imageCount];

        wzlStream.Position = WzlHeaderSizeBytes;

        var index = new int[imageCount];
        long position = wzlStream.Position;

        for (int i = 0; i < index.Length; i++)
        {
            if (position + ImageInfoSizeBytes > wzlStream.Length)
                break;

            if (position > int.MaxValue)
                break;

            wzlStream.Position = position;
            if (!TryReadImageInfo(wzlStream, out WzlImageInfo info))
                break;

            int width = info.Width;
            int height = info.Height;
            if (width <= 0 || height <= 0)
                break;

            int bitCount = ResolveBitCount(info.Enc1);
            if (bitCount == 0)
                break;

            long blockBytes;
            if (info.CompressedLength > 0)
            {
                blockBytes = ImageInfoSizeBytes + info.CompressedLength;
            }
            else
            {
                int bytesPerPixel = bitCount / 8;
                blockBytes = ImageInfoSizeBytes + (long)width * height * bytesPerPixel;
            }

            if (blockBytes <= 0)
                break;

            index[i] = (int)position;
            position += blockBytes;
        }

        return index;
    }

    private static bool TryReadImageInfo(Stream stream, out WzlImageInfo info)
    {
        info = default;
        if (stream.Position + ImageInfoSizeBytes > stream.Length)
            return false;

        Span<byte> buf = stackalloc byte[ImageInfoSizeBytes];
        stream.ReadExactly(buf);

        info.Enc1 = buf[0];
        info.Enc2 = buf[1];
        info.Width = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(4, 2));
        info.Height = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(6, 2));
        info.Px = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(8, 2));
        info.Py = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(10, 2));
        info.CompressedLength = BinaryPrimitives.ReadUInt32LittleEndian(buf.Slice(12, 4));
        return true;
    }

    private static int ResolveBitCount(byte enc1) => enc1 switch
    {
        3 => 8,
        5 => 16,
        6 => 24,
        7 => 32,
        _ => 0
    };

    private static byte[] ZlibDecompress(ReadOnlySpan<byte> compressed)
    {
        using var src = new MemoryStream(compressed.ToArray(), writable: false);
        using var zlib = new ZLibStream(src, CompressionMode.Decompress, leaveOpen: false);
        using var dst = new MemoryStream();
        zlib.CopyTo(dst);
        return dst.ToArray();
    }

    private static bool TryDecodeToBgra32(ReadOnlySpan<byte> raw, int width, int height, int bitCount, out byte[] bgra)
    {
        bgra = Array.Empty<byte>();

        if (width <= 0 || height <= 0)
            return false;

        int bytesPerPixel = bitCount / 8;
        if (bytesPerPixel <= 0)
            return false;

        if (raw.Length % height != 0)
            return false;

        int stride = raw.Length / height;
        if (stride < checked(width * bytesPerPixel))
            return false;

        bgra = new byte[checked(width * height * 4)];
        ReadOnlySpan<uint> palette = bitCount == 8 ? Mir2MainPalette.Colors : default;

        for (int y = 0; y < height; y++)
        {
            int srcRow = y * stride;
            int dstRow = (height - 1 - y) * width * 4;

            switch (bitCount)
            {
                case 8:
                    Decode8(raw, palette, width, srcRow, bgra, dstRow);
                    break;
                case 16:
                    Decode16(raw, width, srcRow, bgra, dstRow);
                    break;
                case 24:
                    Decode24(raw, width, srcRow, bgra, dstRow);
                    break;
                case 32:
                    Decode32(raw, width, srcRow, bgra, dstRow);
                    break;
                default:
                    return false;
            }
        }

        return true;
    }

    private static bool TryDecodeR5G6B5Alpha(ReadOnlySpan<byte> raw, int width, int height, out byte[] bgra)
    {
        bgra = Array.Empty<byte>();

        if (width <= 0 || height <= 0)
            return false;

        int colorStride = checked(width * 2);
        int colorBytes = checked(colorStride * height);

        int alphaStride = width / 2;
        int alphaBytes = checked(alphaStride * height + 1);

        if (raw.Length < colorBytes + alphaBytes)
            return false;

        ReadOnlySpan<byte> color = raw.Slice(0, colorBytes);
        ReadOnlySpan<byte> alpha = raw.Slice(colorBytes);

        bgra = new byte[checked(width * height * 4)];

        for (int y = 0; y < height; y++)
        {
            int srcRow = y * colorStride;
            int dstRow = (height - 1 - y) * width * 4;

            int alphaRow = (height - 1 - y) * alphaStride;
            int alphaPos = alphaRow;
            bool lo = true;

            for (int x = 0; x < width; x++)
            {
                ushort value = BinaryPrimitives.ReadUInt16LittleEndian(color.Slice(srcRow + (x * 2), 2));

                int o = dstRow + (x * 4);
                if (value == 0)
                {
                    bgra[o + 0] = 0;
                    bgra[o + 1] = 0;
                    bgra[o + 2] = 0;
                    bgra[o + 3] = 0;
                }
                else
                {
                    byte r = (byte)((value & 0xF800) >> 8);
                    byte g = (byte)((value & 0x07E0) >> 3);
                    byte b = (byte)((value & 0x001F) << 3);

                    byte aByte = alpha[alphaPos];
                    byte a = lo ? (byte)((aByte & 0x0F) * 16) : (byte)(((aByte & 0xF0) >> 4) * 16);

                    bgra[o + 0] = b;
                    bgra[o + 1] = g;
                    bgra[o + 2] = r;
                    bgra[o + 3] = a;
                }

                if (lo)
                {
                    alphaPos++;
                    lo = false;
                }
                else
                {
                    lo = true;
                }
            }
        }

        return true;
    }

    private static void Decode8(ReadOnlySpan<byte> raw, ReadOnlySpan<uint> palette, int width, int srcRow, byte[] dst, int dstRow)
    {
        for (int x = 0; x < width; x++)
        {
            byte index = raw[srcRow + x];
            uint color = index == 0 ? 0u : palette[index];

            int o = dstRow + (x * 4);
            dst[o + 0] = (byte)(color & 0xFF);
            dst[o + 1] = (byte)((color >> 8) & 0xFF);
            dst[o + 2] = (byte)((color >> 16) & 0xFF);
            dst[o + 3] = (byte)((color >> 24) & 0xFF);
        }
    }

    private static void Decode16(ReadOnlySpan<byte> raw, int width, int srcRow, byte[] dst, int dstRow)
    {
        for (int x = 0; x < width; x++)
        {
            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(raw.Slice(srcRow + (x * 2), 2));
            int o = dstRow + (x * 4);

            if (value == 0)
            {
                dst[o + 0] = 0;
                dst[o + 1] = 0;
                dst[o + 2] = 0;
                dst[o + 3] = 0;
                continue;
            }

            byte r = (byte)((value & 0xF800) >> 8);
            byte g = (byte)((value & 0x07E0) >> 3);
            byte b = (byte)((value & 0x001F) << 3);

            dst[o + 0] = b;
            dst[o + 1] = g;
            dst[o + 2] = r;
            dst[o + 3] = 255;
        }
    }

    private static void Decode24(ReadOnlySpan<byte> raw, int width, int srcRow, byte[] dst, int dstRow)
    {
        for (int x = 0; x < width; x++)
        {
            int src = srcRow + (x * 3);
            byte b = raw[src + 0];
            byte g = raw[src + 1];
            byte r = raw[src + 2];
            byte a = (b | g | r) == 0 ? (byte)0 : (byte)255;

            int o = dstRow + (x * 4);
            dst[o + 0] = b;
            dst[o + 1] = g;
            dst[o + 2] = r;
            dst[o + 3] = a;
        }
    }

    private static void Decode32(ReadOnlySpan<byte> raw, int width, int srcRow, byte[] dst, int dstRow)
    {
        for (int x = 0; x < width; x++)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(raw.Slice(srcRow + (x * 4), 4));

            int o = dstRow + (x * 4);
            if (value == 0)
            {
                dst[o + 0] = 0;
                dst[o + 1] = 0;
                dst[o + 2] = 0;
                dst[o + 3] = 0;
                continue;
            }

            dst[o + 0] = (byte)(value & 0xFF);
            dst[o + 1] = (byte)((value >> 8) & 0xFF);
            dst[o + 2] = (byte)((value >> 16) & 0xFF);
            dst[o + 3] = 255;
        }
    }

    private struct WzlImageInfo
    {
        public byte Enc1;
        public byte Enc2;
        public short Width;
        public short Height;
        public short Px;
        public short Py;
        public uint CompressedLength;
    }
}

