using System.Buffers.Binary;
using System.Text;

namespace MirClient.Protocol.Security;







public static class HardwareTokenCodec
{
    public const uint DefaultMagicCode = 0x13F13F13;
    public const string DefaultKey = "openmir2";

    public const int Md5DigestLength = 16;
    public const int HeaderLength = 4 + Md5DigestLength; 
    public const int TokenLength = 2 + HeaderLength * 2; 

    public static string Encode(
        ReadOnlySpan<byte> md5Digest,
        uint magicCode = DefaultMagicCode,
        string key = DefaultKey,
        int? offset = null)
    {
        if (md5Digest.Length != Md5DigestLength)
            throw new ArgumentException($"MD5 digest must be {Md5DigestLength} bytes.", nameof(md5Digest));

        Span<byte> header = stackalloc byte[HeaderLength];
        BinaryPrimitives.WriteUInt32LittleEndian(header, magicCode);
        md5Digest.CopyTo(header[4..]);

        return EncodeHeader(header, key, offset);
    }

    public static string EncodeHeader(ReadOnlySpan<byte> header, string key = DefaultKey, int? offset = null)
    {
        if (header.Length < HeaderLength)
            throw new ArgumentException($"Hardware header must be at least {HeaderLength} bytes.", nameof(header));
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("Key cannot be empty.", nameof(key));

        byte[] keyBytes = Encoding.ASCII.GetBytes(key);
        if (keyBytes.Length == 0)
            throw new ArgumentException("Key cannot be empty.", nameof(key));

        int keyPos = 0;
        int keyLen = keyBytes.Length;

        int seedOffset = offset ?? Random.Shared.Next(256);

        var sb = new StringBuilder(TokenLength);
        sb.Append(seedOffset.ToString("x2"));

        int rollingOffset = seedOffset;
        for (int i = 0; i < HeaderLength; i++)
        {
            int srcAsc = (header[i] + rollingOffset) % 255;
            srcAsc ^= keyBytes[keyPos];
            sb.Append(((byte)srcAsc).ToString("x2"));

            rollingOffset = srcAsc;
            keyPos++;
            if (keyPos >= keyLen)
                keyPos = 0;
        }

        return sb.ToString();
    }

    public static bool TryDecodeHeader(string token, out byte[] header, string key = DefaultKey)
    {
        header = Array.Empty<byte>();
        if (string.IsNullOrWhiteSpace(token))
            return false;
        if (string.IsNullOrEmpty(key))
            return false;

        ReadOnlySpan<char> src = token.Trim().AsSpan();
        if (src.Length < TokenLength)
            return false;
        if ((src.Length & 1) != 0)
            return false;

        if (!TryParseHexByte(src[..2], out int rollingOffset))
            return false;

        byte[] keyBytes = Encoding.ASCII.GetBytes(key);
        if (keyBytes.Length == 0)
            return false;

        header = new byte[HeaderLength];

        int keyPos = 0;
        int keyLen = keyBytes.Length;
        int outPos = 0;

        for (int srcPos = 2; srcPos + 1 < src.Length && outPos < HeaderLength; srcPos += 2)
        {
            if (!TryParseHexByte(src.Slice(srcPos, 2), out int srcAsc))
                return false;

            int tmp = srcAsc ^ keyBytes[keyPos];
            int decoded = tmp <= rollingOffset ? 255 + tmp - rollingOffset : tmp - rollingOffset;
            header[outPos++] = (byte)decoded;

            rollingOffset = srcAsc;
            keyPos++;
            if (keyPos >= keyLen)
                keyPos = 0;
        }

        return outPos == HeaderLength;
    }

    private static bool TryParseHexByte(ReadOnlySpan<char> hex, out int value)
    {
        value = 0;
        if (hex.Length != 2)
            return false;

        int hi = HexValue(hex[0]);
        int lo = HexValue(hex[1]);
        if (hi < 0 || lo < 0)
            return false;

        value = (hi << 4) | lo;
        return true;
    }

    private static int HexValue(char c)
    {
        if (c is >= '0' and <= '9')
            return c - '0';
        if (c is >= 'a' and <= 'f')
            return c - 'a' + 10;
        if (c is >= 'A' and <= 'F')
            return c - 'A' + 10;
        return -1;
    }
}

