using System.Buffers.Binary;

namespace MirClient.Assets.Light;

public sealed class LightDatFile
{
    private LightDatFile(string filePath, int width, int height, byte[] alpha)
    {
        FilePath = filePath;
        Width = width;
        Height = height;
        Alpha = alpha;
    }

    public string FilePath { get; }
    public int Width { get; }
    public int Height { get; }
    public byte[] Alpha { get; }

    public static LightDatFile Read(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path is required.", nameof(filePath));

        filePath = Path.GetFullPath(filePath);
        using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        Span<byte> header = stackalloc byte[8];
        stream.ReadExactly(header);

        int width = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(0, 4));
        int height = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(4, 4));

        if (width <= 0 || height <= 0)
            throw new InvalidDataException($"Invalid light dat size: {width}x{height}.");

        int count = checked(width * height);
        byte[] alpha = new byte[count];
        stream.ReadExactly(alpha);

        return new LightDatFile(filePath, width, height, alpha);
    }

    public byte[] ToBgra32(byte r = 255, byte g = 255, byte b = 255)
    {
        byte[] bgra = new byte[checked(Width * Height * 4)];
        ReadOnlySpan<byte> alpha = Alpha;

        int dst = 0;
        for (int i = 0; i < alpha.Length; i++)
        {
            int scaled = (alpha[i] * 255 + 15) / 30;
            byte intensity = (byte)Math.Clamp(scaled, 0, 255);
            bgra[dst + 0] = (byte)((b * intensity + 127) / 255);
            bgra[dst + 1] = (byte)((g * intensity + 127) / 255);
            bgra[dst + 2] = (byte)((r * intensity + 127) / 255);
            bgra[dst + 3] = 255;
            dst += 4;
        }

        return bgra;
    }
}
