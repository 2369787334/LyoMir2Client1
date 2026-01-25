using System.Buffers.Binary;

namespace MirClient.Assets.Maps;

public readonly record struct MirMapCell(
    ushort BkImg,
    ushort MidImg,
    ushort FrImg,
    byte DoorIndex,
    byte DoorOffset,
    byte AniFrame,
    byte AniTick,
    byte Area,
    byte Light,
    byte Tiles,
    byte SmTiles,
    ushort BkImg2,
    ushort MidImg2,
    ushort FrImg2,
    byte DoorIndex2,
    byte DoorOffset2,
    ushort AniFrame2,
    byte Area2,
    byte Light2,
    byte Tiles2,
    byte SmTiles2,
    byte Temp0,
    byte Temp1,
    byte Temp2,
    byte Temp3,
    byte Temp4,
    byte Temp5,
    byte Temp6,
    byte Temp7)
{
    public bool IsWalkable => (BkImg & 0x8000) == 0 && (FrImg & 0x8000) == 0;

    public int BkIndex => BkImg & 0x7FFF;
    public int MidIndex => MidImg & 0x7FFF;
    public int FrIndex => FrImg & 0x7FFF;

    internal static MirMapCell Parse(ReadOnlySpan<byte> data, MirMapFormat format)
    {
        ushort bk = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(0, 2));
        ushort mid = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(2, 2));
        ushort fr = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(4, 2));

        byte doorIndex = data[6];
        byte doorOffset = data[7];
        byte aniFrame = data[8];
        byte aniTick = data[9];
        byte area = data[10];
        byte light = data[11];

        byte tiles = 0;
        byte smTiles = 0;

        ushort bk2 = 0;
        ushort mid2 = 0;
        ushort fr2 = 0;
        byte doorIndex2 = 0;
        byte doorOffset2 = 0;
        ushort aniFrame2 = 0;
        byte area2 = 0;
        byte light2 = 0;
        byte tiles2 = 0;
        byte smTiles2 = 0;
        byte temp0 = 0;
        byte temp1 = 0;
        byte temp2 = 0;
        byte temp3 = 0;
        byte temp4 = 0;
        byte temp5 = 0;
        byte temp6 = 0;
        byte temp7 = 0;

        if (format is MirMapFormat.V2 or MirMapFormat.V6)
        {
            tiles = data[12];
            smTiles = data[13];
        }

        if (format == MirMapFormat.V6)
        {
            bk2 = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(14, 2));
            mid2 = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(16, 2));
            fr2 = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(18, 2));
            doorIndex2 = data[20];
            doorOffset2 = data[21];
            aniFrame2 = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(22, 2));
            area2 = data[24];
            light2 = data[25];
            tiles2 = data[26];
            smTiles2 = data[27];
            temp0 = data[28];
            temp1 = data[29];
            temp2 = data[30];
            temp3 = data[31];
            temp4 = data[32];
            temp5 = data[33];
            temp6 = data[34];
            temp7 = data[35];
        }

        return new MirMapCell(
            bk,
            mid,
            fr,
            doorIndex,
            doorOffset,
            aniFrame,
            aniTick,
            area,
            light,
            tiles,
            smTiles,
            bk2,
            mid2,
            fr2,
            doorIndex2,
            doorOffset2,
            aniFrame2,
            area2,
            light2,
            tiles2,
            smTiles2,
            temp0,
            temp1,
            temp2,
            temp3,
            temp4,
            temp5,
            temp6,
            temp7);
    }
}
