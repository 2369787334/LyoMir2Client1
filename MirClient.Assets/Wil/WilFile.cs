using System.Buffers.Binary;
using System.IO.Compression;
using MirClient.Assets.Palettes;

namespace MirClient.Assets.Wil;

public sealed class WilFile : IDisposable
{
    private readonly FileStream _wilStream;
    private readonly int[] _index;
    private readonly WilHeader _header;
    private readonly int _bitCount;

    private WilFile(string wilPath, string? wixPath, FileStream wilStream, int[] index, WilHeader header, int bitCount)
    {
        WilPath = wilPath;
        WixPath = wixPath;
        _wilStream = wilStream;
        _index = index;
        _header = header;
        _bitCount = bitCount;
    }

    public string WilPath { get; }
    public string? WixPath { get; }
    public WilVersion Version => _header.Version;
    public int ImageCount => _header.ImageCount;
    public int BitCount => _bitCount;

    public static WilFile Open(string wilPath, string? wixPath = null)
    {
        if (string.IsNullOrWhiteSpace(wilPath))
            throw new ArgumentException("WIL path is required.", nameof(wilPath));

        wilPath = Path.GetFullPath(wilPath);
        wixPath ??= Path.ChangeExtension(wilPath, ".wix");
        wixPath = string.IsNullOrWhiteSpace(wixPath) ? null : Path.GetFullPath(wixPath);

        var wilStream = new FileStream(wilPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            WilHeader header = ReadHeader(wilStream);
            int bitCount = ResolveBitCount(header.ColorCount, header.VerFlag);

            int[] index = wixPath != null && File.Exists(wixPath)
                ? ReadIndex(wixPath, header.ImageCount, header.Version)
                : new int[header.ImageCount];

            return new WilFile(wilPath, wixPath, wilStream, index, header, bitCount);
        }
        catch
        {
            wilStream.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        _wilStream.Dispose();
    }

    public bool HasImage(int imageIndex)
    {
        if ((uint)imageIndex >= (uint)_index.Length)
            return false;

        int position = _index[imageIndex];
        return position > 0 && position < _wilStream.Length;
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
            if (position >= _wilStream.Length)
                return false;

            _wilStream.Position = position;

            if (!TryReadImageInfo(_wilStream, _header.Version, out WilImageInfo info, out int infoSizeBytes))
                return false;

            if (info.Width <= 0 || info.Height <= 0)
                return false;

            int stride = WidthBytes(info.Width, _bitCount);
            int expectedRawBytes = checked(stride * info.Height);

            if ((long)position + infoSizeBytes + expectedRawBytes > _wilStream.Length && info.NSize == 0)
                return false;

            byte[] raw = ReadImagePixelsRaw(_wilStream, _header.Version, info.NSize, expectedRawBytes);
            byte[] bgra = DecodeToBgra32(raw, info.Width, info.Height, stride, _bitCount);

            image = new WilImage(info.Width, info.Height, info.Px, info.Py, bgra);
            return true;
        }
        catch
        {
            image = null!;
            return false;
        }
    }

    private static byte[] ReadImagePixelsRaw(Stream stream, WilVersion version, uint nSize, int expectedRawBytes)
    {
        if (version == WilVersion.Version2 && nSize > 0)
        {
            if (nSize < 6)
                throw new InvalidDataException($"Invalid WIL v2 block size: {nSize}.");

            long remaining = stream.Length - stream.Position;
            if (nSize > remaining)
                throw new EndOfStreamException("WIL v2 block exceeds remaining file length.");

            int compressedFlag = stream.ReadByte();
            if (compressedFlag < 0)
                throw new EndOfStreamException();

            Span<byte> skip = stackalloc byte[5];
            stream.ReadExactly(skip);

            int payloadLen = checked((int)nSize - 6);
            byte[] payload = new byte[payloadLen];
            stream.ReadExactly(payload);

            if (compressedFlag == 8)
            {
                using var src = new MemoryStream(payload, writable: false);
                using var deflate = new DeflateStream(src, CompressionMode.Decompress, leaveOpen: false);
                byte[] raw = new byte[expectedRawBytes];
                deflate.ReadExactly(raw);
                return raw;
            }

            if (payloadLen != expectedRawBytes)
                throw new InvalidDataException($"Unexpected raw payload length {payloadLen}, expected {expectedRawBytes}.");

            return payload;
        }

        byte[] rawUncompressed = new byte[expectedRawBytes];
        stream.ReadExactly(rawUncompressed);
        return rawUncompressed;
    }

    private static byte[] DecodeToBgra32(ReadOnlySpan<byte> raw, int width, int height, int stride, int bitCount)
    {
        byte[] bgra = new byte[checked(width * height * 4)];

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
                    throw new NotSupportedException($"Unsupported WIL bit depth: {bitCount}.");
            }
        }

        return bgra;
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

    private static int WidthBytes(int width, int bitCount) => checked((((width * bitCount) + 31) >> 5) * 4);

    private static bool TryReadImageInfo(Stream stream, WilVersion version, out WilImageInfo info, out int bytesRead)
    {
        info = default;
        bytesRead = 0;

        Span<byte> buffer = stackalloc byte[16];
        int size = version switch
        {
            WilVersion.Version2 => 16,
            WilVersion.Version0 => 12,
            _ => 8
        };

        stream.ReadExactly(buffer[..size]);
        bytesRead = size;

        info.Width = BinaryPrimitives.ReadUInt16LittleEndian(buffer);
        info.Height = BinaryPrimitives.ReadUInt16LittleEndian(buffer.Slice(2, 2));
        info.Px = BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(4, 2));
        info.Py = BinaryPrimitives.ReadInt16LittleEndian(buffer.Slice(6, 2));

        if (size >= 12)
        {
            info.ImageVersion = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(8, 4));
        }

        if (size == 16)
        {
            info.NSize = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(12, 4));
        }

        return true;
    }

    private static WilHeader ReadHeader(Stream stream)
    {
        stream.Position = 0;
        Span<byte> header = stackalloc byte[60];
        stream.ReadExactly(header);

        int imageCount = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(44, 4));
        int colorCount = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(48, 4));
        int paletteSize = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(52, 4));
        int verFlag = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(56, 4));

        WilVersion version = verFlag switch
        {
            0 => WilVersion.Version1,
            0x20 => WilVersion.Version2,
            _ => WilVersion.Version0
        };

        return new WilHeader(imageCount, colorCount, paletteSize, verFlag, version);
    }

    private static int ResolveBitCount(int colorCount, int verFlag)
    {
        if (verFlag == 0x20)
            return 16;

        return colorCount switch
        {
            256 => 8,
            65536 => 16,
            16777216 => 24,
            > 16777216 => 32,
            _ => throw new NotSupportedException($"Unsupported WIL color count: {colorCount}.")
        };
    }

    private static int[] ReadIndex(string wixPath, int imageCount, WilVersion version)
    {
        int headerBytes = version == WilVersion.Version1 ? 48 : 52;
        var index = new int[imageCount];

        using var fs = new FileStream(wixPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (fs.Length < headerBytes)
            return index;

        fs.Position = headerBytes;

        Span<byte> buf = stackalloc byte[4];
        for (int i = 0; i < index.Length; i++)
        {
            try
            {
                fs.ReadExactly(buf);
            }
            catch (EndOfStreamException)
            {
                break;
            }

            index[i] = BinaryPrimitives.ReadInt32LittleEndian(buf);
        }

        return index;
    }

    private readonly record struct WilHeader(int ImageCount, int ColorCount, int PaletteSize, int VerFlag, WilVersion Version);

    private struct WilImageInfo
    {
        public int Width;
        public int Height;
        public short Px;
        public short Py;
        public uint ImageVersion;
        public uint NSize;
    }
}
