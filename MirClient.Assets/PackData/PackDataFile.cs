using System.Buffers.Binary;
using System.IO.Compression;
using MirClient.Assets.Palettes;
using MirClient.Protocol.Startup;
using MirClient.Protocol.Text;
using StbImageSharp;

namespace MirClient.Assets.PackData;

public sealed class PackDataFile : IDisposable
{
    private const int PlainHeaderSizeBytes = 72; 
    private const int EncryptedHeaderSizeBytes = 80; 

    private const int PlainImageInfoSizeBytes = 14; 
    private const int EncryptedImageInfoSizeBytes = 22; 

    private const string HeaderKey = "7BB2FA4F-2A6A-4632-B72F-F98D440E8C36";
    private const string ImageInfoKey = "CFBA39C1-72A6-4171-9FF0-CF1920DD76F3";

    private readonly FileStream _stream;
    private readonly int[] _index;
    private readonly int _headerBlockSizeBytes;

    private PackDataFile(string filePath, FileStream stream, PackDataHeader header, int headerBlockSizeBytes, int[] index)
    {
        FilePath = filePath;
        _stream = stream;
        Header = header;
        _headerBlockSizeBytes = headerBlockSizeBytes;
        _index = index;
    }

    public string FilePath { get; }
    public PackDataHeader Header { get; }
    public int ImageCount => Header.ImageCount;

    public static PackDataFile Open(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        string fullPath = Path.GetFullPath(filePath);
        var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        try
        {
            if (!TryReadEncryptedHeader(stream, out PackDataHeader header))
            {
                header = ReadPlainHeader(stream);
                int[] indexPlain = ReadIndex(stream, header, PlainHeaderSizeBytes);
                return new PackDataFile(fullPath, stream, header, PlainHeaderSizeBytes, indexPlain);
            }

            int[] index = ReadIndex(stream, header, EncryptedHeaderSizeBytes);
            return new PackDataFile(fullPath, stream, header, EncryptedHeaderSizeBytes, index);
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    public void Dispose() => _stream.Dispose();

    public bool HasImage(int imageIndex)
    {
        if ((uint)imageIndex >= (uint)_index.Length)
            return false;

        int position = _index[imageIndex];
        return position > 0 && position < _stream.Length;
    }

    public bool TryDecodeImage(int imageIndex, out PackDataImage image)
    {
        image = null!;

        if ((uint)imageIndex >= (uint)_index.Length)
            return false;

        int position = _index[imageIndex];
        if (position <= 0)
            return false;

        try
        {
            if (position >= _stream.Length)
                return false;

            _stream.Position = position;
            if (!TryReadImageInfo(_stream, Header.XVersion, out PackDataImageInfo info, out int imageInfoBlockSizeBytes))
                return false;

            if (info.Width <= 0 || info.Height <= 0)
                return false;

            if (info.GraphicType == PackDataGraphicType.None)
                return false;

            if (info.CompressedLength == 0)
                return false;

            long dataStart = _stream.Position;
            long dataEnd = dataStart + info.CompressedLength;
            if (dataEnd > _stream.Length)
                return false;

            byte[] data = new byte[info.CompressedLength];
            _stream.ReadExactly(data);

            byte[] bgra = info.GraphicType switch
            {
                PackDataGraphicType.RealPng => DecodePngToBgra32(data),
                PackDataGraphicType.Png when info.BitCount == 32 => DecodeZlibToBgra32(data, info.Width, info.Height, info.BitCount, preserveAlpha32: true),
                _ => DecodeZlibToBgra32(data, info.Width, info.Height, info.BitCount, preserveAlpha32: false)
            };

            image = new PackDataImage(info.Width, info.Height, info.Px, info.Py, bgra);
            return true;
        }
        catch
        {
            image = null!;
            return false;
        }
    }

    private static bool TryReadEncryptedHeader(Stream stream, out PackDataHeader header)
    {
        header = default;

        if (stream.Length < EncryptedHeaderSizeBytes)
            return false;

        stream.Position = 0;
        Span<byte> enc = stackalloc byte[EncryptedHeaderSizeBytes];
        stream.ReadExactly(enc);

        byte[] dec = LauncherParamCodec.DecodeData(enc, HeaderKey);
        if (dec.Length < PlainHeaderSizeBytes)
            return false;

        header = ParseHeader(dec.AsSpan(0, PlainHeaderSizeBytes));

        if (header.ImageCount < 0 || header.ImageCount > 10_000_000)
            return false;

        if (header.XVersion is not 0 and not 1)
            return false;

        if (header.IndexOffset != 0)
        {
            if (header.IndexOffset < EncryptedHeaderSizeBytes)
                return false;
            if (header.IndexOffset > stream.Length)
                return false;
            long indexBytesRequired = (long)header.ImageCount * 4;
            if (indexBytesRequired > 0 && header.IndexOffset + indexBytesRequired > stream.Length)
                return false;
        }

        return true;
    }

    private static PackDataHeader ReadPlainHeader(Stream stream)
    {
        if (stream.Length < PlainHeaderSizeBytes)
            throw new InvalidDataException("PackData header too short.");

        stream.Position = 0;
        Span<byte> buf = stackalloc byte[PlainHeaderSizeBytes];
        stream.ReadExactly(buf);
        return ParseHeader(buf);
    }

    private static PackDataHeader ParseHeader(ReadOnlySpan<byte> header)
    {
        string title = ReadShortString(header.Slice(0, 41), maxChars: 40);
        int imageCount = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(44, 4));
        int indexOffset = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(48, 4));
        ushort xVersion = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(52, 2));
        string password = ReadShortString(header.Slice(54, 17), maxChars: 16);
        return new PackDataHeader(title, imageCount, indexOffset, xVersion, password);
    }

    private static int[] ReadIndex(Stream stream, PackDataHeader header, int defaultOffset)
    {
        int imageCount = header.ImageCount;
        if (imageCount <= 0)
            return Array.Empty<int>();

        int indexOffset = header.IndexOffset;
        if (indexOffset <= 0)
            indexOffset = defaultOffset;

        if (indexOffset >= stream.Length)
            return new int[imageCount];

        stream.Position = indexOffset;

        var index = new int[imageCount];
        Span<byte> buf = stackalloc byte[4];

        for (int i = 0; i < index.Length; i++)
        {
            if (stream.Position + 4 > stream.Length)
            {
                index[i] = 0;
                continue;
            }

            stream.ReadExactly(buf);
            index[i] = BinaryPrimitives.ReadInt32LittleEndian(buf);
        }

        return index;
    }

    private static bool TryReadImageInfo(Stream stream, ushort xVersion, out PackDataImageInfo info, out int blockSizeBytes)
    {
        info = default;
        blockSizeBytes = 0;

        if (xVersion == 0)
        {
            Span<byte> buf = stackalloc byte[PlainImageInfoSizeBytes];
            stream.ReadExactly(buf);
            blockSizeBytes = PlainImageInfoSizeBytes;
            info = ParseImageInfo(buf);
            return true;
        }

        if (xVersion == 1)
        {
            Span<byte> enc = stackalloc byte[EncryptedImageInfoSizeBytes];
            stream.ReadExactly(enc);
            blockSizeBytes = EncryptedImageInfoSizeBytes;
            byte[] dec = LauncherParamCodec.DecodeData(enc, ImageInfoKey);
            if (dec.Length < PlainImageInfoSizeBytes)
                return false;
            info = ParseImageInfo(dec.AsSpan(0, PlainImageInfoSizeBytes));
            return true;
        }

        return false;
    }

    private static PackDataImageInfo ParseImageInfo(ReadOnlySpan<byte> buf)
    {
        int width = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(0, 2));
        int height = BinaryPrimitives.ReadUInt16LittleEndian(buf.Slice(2, 2));
        byte bitCount = buf[4];
        short px = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(5, 2));
        short py = BinaryPrimitives.ReadInt16LittleEndian(buf.Slice(7, 2));
        uint len = BinaryPrimitives.ReadUInt32LittleEndian(buf.Slice(9, 4));
        var graphicType = (PackDataGraphicType)buf[13];
        return new PackDataImageInfo(width, height, bitCount, px, py, len, graphicType);
    }

    private static string ReadShortString(ReadOnlySpan<byte> buffer, int maxChars)
    {
        if (buffer.IsEmpty)
            return string.Empty;

        int len = buffer[0];
        if (len <= 0)
            return string.Empty;

        len = Math.Clamp(len, 0, maxChars);
        ReadOnlySpan<byte> bytes = buffer.Slice(1, Math.Min(len, buffer.Length - 1));
        return GbkEncoding.Instance.GetString(bytes);
    }

    private static byte[] DecodeZlibToBgra32(ReadOnlySpan<byte> compressed, int width, int height, byte bitCount, bool preserveAlpha32)
    {
        byte[] raw = ZlibDecompress(compressed);
        if (height <= 0 || raw.Length % height != 0)
            throw new InvalidDataException("Decompressed pixel buffer has invalid size.");

        int stride = raw.Length / height;
        return DecodeToBgra32(raw, width, height, stride, bitCount, preserveAlpha32);
    }

    private static byte[] ZlibDecompress(ReadOnlySpan<byte> compressed)
    {
        using var src = new MemoryStream(compressed.ToArray(), writable: false);
        using var zlib = new ZLibStream(src, CompressionMode.Decompress, leaveOpen: false);
        using var dst = new MemoryStream();
        zlib.CopyTo(dst);
        return dst.ToArray();
    }

    private static byte[] DecodePngToBgra32(ReadOnlySpan<byte> pngData)
    {
        using var ms = new MemoryStream(pngData.ToArray(), writable: false);
        ImageResult img = ImageResult.FromStream(ms, ColorComponents.RedGreenBlueAlpha);

        byte[] bgra = new byte[checked(img.Width * img.Height * 4)];
        ReadOnlySpan<byte> rgba = img.Data;

        int dst = 0;
        for (int i = 0; i < rgba.Length; i += 4)
        {
            byte r = rgba[i + 0];
            byte g = rgba[i + 1];
            byte b = rgba[i + 2];
            byte a = rgba[i + 3];

            bgra[dst + 0] = b;
            bgra[dst + 1] = g;
            bgra[dst + 2] = r;
            bgra[dst + 3] = a;
            dst += 4;
        }

        return bgra;
    }

    private static byte[] DecodeToBgra32(ReadOnlySpan<byte> raw, int width, int height, int stride, byte bitCount, bool preserveAlpha32)
    {
        if (width <= 0 || height <= 0)
            throw new ArgumentOutOfRangeException(nameof(width), "Image size must be positive.");

        int bytesPerPixel = bitCount switch
        {
            8 => 1,
            16 => 2,
            24 => 3,
            32 => 4,
            _ => throw new NotSupportedException($"Unsupported PackData bit depth: {bitCount}.")
        };

        if (stride < checked(width * bytesPerPixel))
            throw new InvalidDataException("Image stride is smaller than width.");

        byte[] bgra = new byte[checked(width * height * 4)];

        for (int y = 0; y < height; y++)
        {
            int srcRow = y * stride;
            int dstRow = y * width * 4;

            switch (bitCount)
            {
                case 8:
                    Decode8(raw, width, srcRow, bgra, dstRow);
                    break;
                case 16:
                    Decode16(raw, width, srcRow, bgra, dstRow);
                    break;
                case 24:
                    Decode24(raw, width, srcRow, bgra, dstRow);
                    break;
                case 32:
                    if (preserveAlpha32)
                        Copy32(raw, width, srcRow, bgra, dstRow);
                    else
                        Decode32WithForcedAlpha(raw, width, srcRow, bgra, dstRow);
                    break;
            }
        }

        return bgra;
    }

    private static void Decode8(ReadOnlySpan<byte> raw, int width, int srcRow, byte[] dst, int dstRow)
    {
        ReadOnlySpan<uint> palette = Mir2MainPalette.Colors;
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

    private static void Copy32(ReadOnlySpan<byte> raw, int width, int srcRow, byte[] dst, int dstRow)
    {
        raw.Slice(srcRow, width * 4).CopyTo(dst.AsSpan(dstRow, width * 4));
    }

    private static void Decode32WithForcedAlpha(ReadOnlySpan<byte> raw, int width, int srcRow, byte[] dst, int dstRow)
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
}
