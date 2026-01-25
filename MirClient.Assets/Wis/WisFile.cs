using System.Buffers.Binary;
using MirClient.Assets.Palettes;
using MirClient.Assets.Wil;

namespace MirClient.Assets.Wis;

public sealed class WisFile : IDisposable
{
    private const int HeaderSizeBytes = 12;
    private const int IndexEntrySizeBytes = 12;
    private const int MaxIndexEntryLengthBytes = 64 * 1024 * 1024;

    private readonly FileStream _wisStream;
    private readonly int[] _offsets;
    private readonly int[] _lengths;

    private WisFile(string wisPath, FileStream wisStream, int[] offsets, int[] lengths)
    {
        WisPath = wisPath;
        _wisStream = wisStream;
        _offsets = offsets;
        _lengths = lengths;
    }

    public string WisPath { get; }
    public int ImageCount => _offsets.Length;

    public static WisFile Open(string wisPath)
    {
        if (string.IsNullOrWhiteSpace(wisPath))
            throw new ArgumentException("WIS path is required.", nameof(wisPath));

        wisPath = Path.GetFullPath(wisPath);

        var wisStream = new FileStream(wisPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        try
        {
            (long tableStart, int entryCount) = FindIndexTable(wisStream);

            if (entryCount <= 0)
                throw new InvalidDataException("WIS index table not found or empty.");

            long tableBytes = wisStream.Length - tableStart;
            if (tableBytes <= 0 || (tableBytes % IndexEntrySizeBytes) != 0)
                throw new InvalidDataException($"Invalid WIS index table length: {tableBytes} bytes.");

            if (tableBytes > int.MaxValue)
                throw new NotSupportedException($"WIS index table too large: {tableBytes} bytes.");

            var table = new byte[(int)tableBytes];
            wisStream.Position = tableStart;
            wisStream.ReadExactly(table);

            var offsets = new int[entryCount];
            var lengths = new int[entryCount];

            for (int i = 0; i < entryCount; i++)
            {
                int baseOffset = i * IndexEntrySizeBytes;
                offsets[i] = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(baseOffset, 4)));
                lengths[i] = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(table.AsSpan(baseOffset + 4, 4)));
            }

            return new WisFile(wisPath, wisStream, offsets, lengths);
        }
        catch
        {
            wisStream.Dispose();
            throw;
        }
    }

    public void Dispose() => _wisStream.Dispose();

    public bool HasImage(int imageIndex)
    {
        if ((uint)imageIndex >= (uint)_offsets.Length)
            return false;

        int entryOffset = _offsets[imageIndex];
        int entryLength = _lengths[imageIndex];
        if (entryOffset <= 0 || entryLength <= 0)
            return false;

        return (long)entryOffset + entryLength <= _wisStream.Length;
    }

    public bool TryDecodeImage(int imageIndex, out WilImage image)
    {
        image = null!;

        if ((uint)imageIndex >= (uint)_offsets.Length)
            return false;

        int entryOffset = _offsets[imageIndex];
        int entryLength = _lengths[imageIndex];
        if (entryOffset <= 0 || entryLength <= 0)
            return false;

        try
        {
            if ((long)entryOffset + entryLength > _wisStream.Length)
                return false;

            _wisStream.Position = entryOffset;
            byte[] block = new byte[entryLength];
            _wisStream.ReadExactly(block);

            if (block.Length < HeaderSizeBytes)
                return false;

            uint type = BinaryPrimitives.ReadUInt32LittleEndian(block);
            int width = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(4, 2));
            int height = BinaryPrimitives.ReadUInt16LittleEndian(block.AsSpan(6, 2));
            short px = BinaryPrimitives.ReadInt16LittleEndian(block.AsSpan(8, 2));
            short py = BinaryPrimitives.ReadInt16LittleEndian(block.AsSpan(10, 2));

            if (width <= 0 || height <= 0)
                return false;

            int pixelCount = checked(width * height);
            if (pixelCount <= 0)
                return false;

            ReadOnlySpan<byte> payload = block.AsSpan(HeaderSizeBytes);

            byte[] indices = new byte[pixelCount];
            if (type == 0)
            {
                if (payload.Length < indices.Length)
                    return false;

                payload.Slice(0, indices.Length).CopyTo(indices);
            }
            else if (type == 1)
            {
                DecodeRle(payload, indices);
            }
            else
            {
                return false;
            }

            byte[] bgra = new byte[checked(pixelCount * 4)];
            ReadOnlySpan<uint> palette = Mir2MainPalette.Colors;

            for (int i = 0; i < indices.Length; i++)
            {
                byte index = indices[i];
                uint color = index == 0 ? 0u : palette[index];

                int o = i * 4;
                bgra[o + 0] = (byte)(color & 0xFF);
                bgra[o + 1] = (byte)((color >> 8) & 0xFF);
                bgra[o + 2] = (byte)((color >> 16) & 0xFF);
                bgra[o + 3] = (byte)((color >> 24) & 0xFF);
            }

            image = new WilImage(width, height, px, py, bgra);
            return true;
        }
        catch
        {
            image = null!;
            return false;
        }
    }

    private static void DecodeRle(ReadOnlySpan<byte> payload, Span<byte> output)
    {
        int outPos = 0;
        int inPos = 0;

        while (inPos + 1 < payload.Length && outPos < output.Length)
        {
            byte count = payload[inPos++];
            byte value = payload[inPos++];

            if (count == 0)
            {
                if (value == 0)
                    break;

                int literalLen = value;
                if (literalLen <= 0)
                    continue;

                int availableIn = payload.Length - inPos;
                if (literalLen > availableIn)
                    literalLen = availableIn;

                int availableOut = output.Length - outPos;
                int copy = Math.Min(literalLen, availableOut);

                payload.Slice(inPos, copy).CopyTo(output.Slice(outPos, copy));
                inPos += literalLen;
                outPos += copy;
                continue;
            }

            int run = Math.Min(count, output.Length - outPos);
            output.Slice(outPos, run).Fill(value);
            outPos += run;
        }
    }

    private static (long TableStart, int EntryCount) FindIndexTable(FileStream stream)
    {
        long fileLength = stream.Length;
        if (fileLength < IndexEntrySizeBytes)
            throw new InvalidDataException("WIS file too small.");

        int chunkBytes = 1024 * 1024;
        if (chunkBytes > fileLength)
            chunkBytes = (int)fileLength;

        while (true)
        {
            long chunkStart = fileLength - chunkBytes;

            var buffer = new byte[chunkBytes];
            stream.Position = chunkStart;
            stream.ReadExactly(buffer);

            int scanOffset = buffer.Length - IndexEntrySizeBytes;
            int entriesInTail = 0;

            while (scanOffset >= 0)
            {
                ReadOnlySpan<byte> entry = buffer.AsSpan(scanOffset, IndexEntrySizeBytes);
                uint offset = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(0, 4));
                uint length = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(4, 4));
                uint unk = BinaryPrimitives.ReadUInt32LittleEndian(entry.Slice(8, 4));

                long entryPos = chunkStart + scanOffset;
                if (!IsValidIndexEntry(offset, length, unk, entryPos))
                    break;

                entriesInTail++;
                scanOffset -= IndexEntrySizeBytes;
            }

            if (scanOffset >= 0)
            {
                long tableStart = chunkStart + scanOffset + IndexEntrySizeBytes;
                long tableBytes = fileLength - tableStart;
                int entryCount = checked((int)(tableBytes / IndexEntrySizeBytes));
                return (tableStart, entryCount);
            }

            if (chunkStart == 0)
            {
                int entryCount = checked((int)(fileLength / IndexEntrySizeBytes));
                return (0, entryCount);
            }

            chunkBytes = checked(chunkBytes * 2);
            if (chunkBytes > fileLength)
                chunkBytes = (int)fileLength;
        }
    }

    private static bool IsValidIndexEntry(uint offset, uint length, uint unk, long entryPos)
    {
        if (unk != 0)
            return false;

        if (length > MaxIndexEntryLengthBytes)
            return false;

        if (offset == 0 && length == 0)
            return true;

        if (offset >= entryPos)
            return false;

        return offset + length <= entryPos;
    }
}
