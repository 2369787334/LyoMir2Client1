using System.Buffers.Binary;
using System.Text;

namespace MirClient.Assets.Maps;

public sealed class MirMapFile
{
    private const int HeaderSize = 52;

    private readonly byte[] _cells;
    private readonly int _cellSize;
    private readonly bool[] _walkable;

    private MirMapFile(string mapPath, MirMapHeader header, MirMapFormat format, byte[] cells, int cellSize, bool[] walkable)
    {
        MapPath = mapPath;
        Header = header;
        Format = format;
        _cells = cells;
        _cellSize = cellSize;
        _walkable = walkable;
    }

    public string MapPath { get; }
    public MirMapHeader Header { get; }
    public MirMapFormat Format { get; }
    public int Width => Header.Width;
    public int Height => Header.Height;
    public int CellSizeBytes => _cellSize;

    public static MirMapFile Open(string mapPath)
    {
        if (string.IsNullOrWhiteSpace(mapPath))
            throw new ArgumentException("Map path is required.", nameof(mapPath));

        mapPath = Path.GetFullPath(mapPath);

        using var fs = new FileStream(mapPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        Span<byte> headerBuf = stackalloc byte[HeaderSize];
        fs.ReadExactly(headerBuf);

        MirMapHeader header = ParseHeader(headerBuf);

        long totalCells = (long)header.Width * header.Height;
        if (totalCells <= 0)
            throw new InvalidDataException("Map file has invalid dimensions.");

        long remainingBytes = fs.Length - fs.Position;
        if (remainingBytes <= 0)
            throw new InvalidDataException("Map file has no cell data.");

        MirMapFormat format = header.FormatByte switch
        {
            6 => MirMapFormat.V6,
            2 => MirMapFormat.V2,
            0 => MirMapFormat.Old,
            _ => TryInferFormatByCellSize(remainingBytes, totalCells, out MirMapFormat inferred) ? inferred : MirMapFormat.Old
        };

        int cellSize = format switch
        {
            MirMapFormat.V6 => 36,
            MirMapFormat.V2 => 14,
            _ => 12
        };

        long expectedBytesLong = totalCells * cellSize;
        if (expectedBytesLong <= 0 || expectedBytesLong > int.MaxValue)
            throw new InvalidDataException("Map file is too large.");

        int expectedBytes = (int)expectedBytesLong;
        if (remainingBytes < expectedBytes)
        {
            
            if (!TryInferFormatByRemainingBytes(remainingBytes, totalCells, out format, out cellSize))
            {
                throw new InvalidDataException($"Map file is too small. Need {expectedBytes} bytes of cell data, got {remainingBytes}.");
            }

            expectedBytesLong = totalCells * cellSize;
            if (expectedBytesLong <= 0 || expectedBytesLong > int.MaxValue)
                throw new InvalidDataException("Map file is too large.");

            expectedBytes = (int)expectedBytesLong;
        }

        byte[] cells = new byte[expectedBytes];
        fs.ReadExactly(cells);

        bool[] walkable = BuildWalkableMap(cells, header.Width, header.Height, cellSize);
        return new MirMapFile(mapPath, header, format, cells, cellSize, walkable);
    }

    public bool IsWalkable(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            return false;

        return _walkable[(x * Height) + y];
    }

    public MirMapCell GetCell(int x, int y)
    {
        if ((uint)x >= (uint)Width)
            throw new ArgumentOutOfRangeException(nameof(x));
        if ((uint)y >= (uint)Height)
            throw new ArgumentOutOfRangeException(nameof(y));

        int index = (x * Height) + y;
        int offset = checked(index * _cellSize);
        return MirMapCell.Parse(_cells.AsSpan(offset, _cellSize), Format);
    }

    private static bool[] BuildWalkableMap(ReadOnlySpan<byte> cells, int width, int height, int cellSize)
    {
        var walkable = new bool[checked(width * height)];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                int index = (x * height) + y;
                int offset = index * cellSize;

                ushort bk = BinaryPrimitives.ReadUInt16LittleEndian(cells.Slice(offset + 0, 2));
                ushort fr = BinaryPrimitives.ReadUInt16LittleEndian(cells.Slice(offset + 4, 2));

                bool canWalk = (bk & 0x8000) == 0 && (fr & 0x8000) == 0;
                if (canWalk)
                {
                    byte doorIndex = cells[offset + 6];
                    if ((doorIndex & 0x80) != 0)
                    {
                        byte doorOffset = cells[offset + 7];
                        if ((doorOffset & 0x80) == 0)
                            canWalk = false;
                    }
                }

                walkable[index] = canWalk;
            }
        }

        return walkable;
    }

    private static MirMapHeader ParseHeader(ReadOnlySpan<byte> header)
    {
        ushort width = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(0, 2));
        ushort height = BinaryPrimitives.ReadUInt16LittleEndian(header.Slice(2, 2));
        string title = ReadDelphiShortString(header.Slice(4, 16), 15);

        long dateBits = BinaryPrimitives.ReadInt64LittleEndian(header.Slice(20, 8));
        double oaDate = BitConverter.Int64BitsToDouble(dateBits);
        DateTime updateDate = DateTime.FromOADate(oaDate);

        byte formatByte = header[28];
        return new MirMapHeader(width, height, title, updateDate, formatByte);
    }

    private static bool TryInferFormatByCellSize(long remainingBytes, long totalCells, out MirMapFormat format)
    {
        format = MirMapFormat.Old;
        if (remainingBytes <= 0 || totalCells <= 0)
            return false;

        if (remainingBytes % totalCells != 0)
            return false;

        long cellSize = remainingBytes / totalCells;
        format = cellSize switch
        {
            36 => MirMapFormat.V6,
            14 => MirMapFormat.V2,
            12 => MirMapFormat.Old,
            _ => MirMapFormat.Old
        };
        return cellSize is 12 or 14 or 36;
    }

    private static bool TryInferFormatByRemainingBytes(long remainingBytes, long totalCells, out MirMapFormat format, out int cellSize)
    {
        format = MirMapFormat.Old;
        cellSize = 12;

        if (remainingBytes <= 0 || totalCells <= 0)
            return false;

        long expectedV6 = totalCells * 36;
        if (expectedV6 > 0 && remainingBytes >= expectedV6)
        {
            format = MirMapFormat.V6;
            cellSize = 36;
            return true;
        }

        long expectedV2 = totalCells * 14;
        if (expectedV2 > 0 && remainingBytes >= expectedV2)
        {
            format = MirMapFormat.V2;
            cellSize = 14;
            return true;
        }

        long expectedOld = totalCells * 12;
        if (expectedOld > 0 && remainingBytes >= expectedOld)
        {
            format = MirMapFormat.Old;
            cellSize = 12;
            return true;
        }

        return false;
    }

    private static string ReadDelphiShortString(ReadOnlySpan<byte> bytes, int maxLen)
    {
        if (bytes.IsEmpty)
            return string.Empty;

        int len = bytes[0];
        if (len > maxLen)
            len = maxLen;

        if (len <= 0)
            return string.Empty;

        return Encoding.Latin1.GetString(bytes.Slice(1, len));
    }
}

public readonly record struct MirMapHeader(ushort Width, ushort Height, string Title, DateTime UpdateDate, byte FormatByte);
