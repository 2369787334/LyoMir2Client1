using System.Runtime.InteropServices;
using System.Text;
using System.Runtime.CompilerServices;
using MirClient.Protocol.Packets;
using MirClient.Protocol.Text;

namespace MirClient.Protocol.Codec;




public static class EdCode
{
    
    private const byte Seed = 0xAC;
    private const byte Base = 0x3C; 
    private const int BufferSize = 10_000; 

    private static readonly Encoding Ascii = Encoding.ASCII;

    public static string EncodeMessage(CmdPack message)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref message, 1));
        return EncodeBytes(bytes);
    }

    public static CmdPack DecodeMessage(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return default;

        byte[] bytes = DecodeBytes(encoded);
        if (bytes.Length < CmdPack.Size)
            return default;

        return MemoryMarshal.Read<CmdPack>(bytes.AsSpan(0, CmdPack.Size));
    }

    public static string EncodeString(string value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        byte[] bytes = GbkEncoding.Instance.GetBytes(value);
        return EncodeBytes(bytes);
    }

    public static string DecodeString(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return string.Empty;

        byte[] bytes = DecodeBytes(encoded);
        int len = IndexOfNull(bytes);
        if (len < 0)
            len = bytes.Length;

        return GbkEncoding.Instance.GetString(bytes, 0, len);
    }

    public static string EncodeBuffer(ReadOnlySpan<byte> buffer)
    {
        if (buffer.IsEmpty)
            return string.Empty;
        if (buffer.Length >= BufferSize)
            return string.Empty;

        return EncodeBytes(buffer);
    }

    public static int GetEncodedLength(int byteCount)
    {
        if (byteCount <= 0)
            return 0;

        int cycles = byteCount / 3;
        int rem = byteCount % 3;
        return (cycles * 4) + (rem == 0 ? 0 : rem + 1);
    }

    public static byte[] DecodeBytes(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return Array.Empty<byte>();

        ReadOnlySpan<char> src = encoded.AsSpan();
        int len = src.Length;

        int cycles = len / 4;
        int bytesLeft = len % 4;
        int outLen = cycles * 3 + bytesLeft switch
        {
            2 => 1,
            3 => 2,
            _ => 0
        };

        byte[] dst = new byte[outLen];
        int dstPos = 0;

        for (int i = 0; i < cycles; i++)
        {
            int p = i * 4;
            byte remainder = unchecked((byte)(src[p + 3] - Base));
            byte temp = unchecked((byte)(src[p + 0] - Base));

            byte c = unchecked((byte)((((temp << 2) & 0xF0) | (remainder & 0x0C) | (temp & 0x03)) ^ Seed));
            dst[dstPos++] = c;

            temp = unchecked((byte)(src[p + 1] - Base));
            c = unchecked((byte)((((temp << 2) & 0xF0) | ((remainder << 2) & 0x0C) | (temp & 0x03)) ^ Seed));
            dst[dstPos++] = c;

            temp = unchecked((byte)(src[p + 2] - Base));
            c = unchecked((byte)(((temp | ((remainder << 2) & 0xC0)) ^ Seed)));
            dst[dstPos++] = c;
        }

        if (bytesLeft == 2)
        {
            byte remainder = unchecked((byte)(src[len - 1] - Base));
            byte temp = unchecked((byte)(src[len - 2] - Base));
            byte c = unchecked((byte)((((temp << 2) & 0xF0) | ((remainder << 2) & 0x0C) | (temp & 0x03)) ^ Seed));
            dst[dstPos++] = c;
        }
        else if (bytesLeft == 3)
        {
            byte remainder = unchecked((byte)(src[len - 1] - Base));

            byte temp = unchecked((byte)(src[len - 3] - Base));
            byte c = unchecked((byte)((((temp << 2) & 0xF0) | (remainder & 0x0C) | (temp & 0x03)) ^ Seed));
            dst[dstPos++] = c;

            temp = unchecked((byte)(src[len - 2] - Base));
            c = unchecked((byte)((((temp << 2) & 0xF0) | ((remainder << 2) & 0x0C) | (temp & 0x03)) ^ Seed));
            dst[dstPos++] = c;
        }

        return dst;
    }

    public static bool TryDecodeBuffer<T>(string encoded, out T value) where T : unmanaged
    {
        value = default;
        if (string.IsNullOrEmpty(encoded))
            return false;

        byte[] bytes = DecodeBytes(encoded);
        if (bytes.Length < Unsafe.SizeOf<T>())
            return false;

        value = MemoryMarshal.Read<T>(bytes);
        return true;
    }

    public static T DecodeBuffer<T>(string encoded) where T : unmanaged
    {
        if (!TryDecodeBuffer(encoded, out T value))
            throw new FormatException($"Encoded buffer is too short for {typeof(T).Name}.");

        return value;
    }

    private static string EncodeBytes(ReadOnlySpan<byte> src)
    {
        if (src.IsEmpty)
            return string.Empty;

        if (src.Length >= BufferSize)
            return string.Empty;

        int cycles = src.Length / 3;
        int rem = src.Length % 3;
        int outLen = cycles * 4 + (rem == 0 ? 0 : rem + 1);

        Span<byte> dst = outLen <= 1024 ? stackalloc byte[outLen] : new byte[outLen];
        int dstPos = 0;

        int no = 2;
        byte remainder = 0;

        foreach (byte raw in src)
        {
            byte c = unchecked((byte)(raw ^ Seed));
            if (no == 6)
            {
                dst[dstPos++] = unchecked((byte)((c & 0x3F) + Base));
                remainder = unchecked((byte)(remainder | ((c >> 2) & 0x30)));
                dst[dstPos++] = unchecked((byte)(remainder + Base));
                remainder = 0;
            }
            else
            {
                byte temp = unchecked((byte)(c >> 2));
                dst[dstPos++] = unchecked((byte)(((temp & 0x3C) | (c & 0x03)) + Base));
                remainder = unchecked((byte)((remainder << 2) | (temp & 0x03)));
            }

            no = no % 6 + 2;
        }

        if (no != 2)
            dst[dstPos++] = unchecked((byte)(remainder + Base));

        return Ascii.GetString(dst[..dstPos]);
    }

    private static int IndexOfNull(byte[] bytes)
    {
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == 0)
                return i;
        }
        return -1;
    }
}
