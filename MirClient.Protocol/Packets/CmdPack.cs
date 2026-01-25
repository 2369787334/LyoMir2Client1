using System.Runtime.InteropServices;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Explicit, Pack = 1, Size = Size)]
public struct CmdPack
{
    public const int Size = 12;

    
    [FieldOffset(0)] public int UID;
    [FieldOffset(4)] public ushort Cmd;
    [FieldOffset(6)] public ushort X;
    [FieldOffset(8)] public ushort Y;
    [FieldOffset(10)] public ushort Direct;

    
    [FieldOffset(0)] public int ID1;
    [FieldOffset(4)] public ushort Cmd1;
    [FieldOffset(6)] public int ID2;

    
    [FieldOffset(0)] public ushort PosX;
    [FieldOffset(2)] public ushort PosY;
    [FieldOffset(4)] public ushort Cmd2;
    [FieldOffset(6)] public ushort IDLo;
    [FieldOffset(8)] public ushort Magic;
    [FieldOffset(10)] public ushort IDHi;

    
    [FieldOffset(0)] public int UID1;
    [FieldOffset(4)] public ushort Cmd3;
    [FieldOffset(6)] public byte B1;
    [FieldOffset(7)] public byte B2;
    [FieldOffset(8)] public byte B3;
    [FieldOffset(9)] public byte B4;

    
    [FieldOffset(0)] public int NID;
    [FieldOffset(4)] public ushort Command;
    [FieldOffset(6)] public ushort Pos;
    [FieldOffset(8)] public ushort Dir;
    [FieldOffset(10)] public ushort WID;

    
    [FieldOffset(0)] public uint Head;
    [FieldOffset(4)] public ushort Cmd4;
    [FieldOffset(6)] public ushort Zero1;
    [FieldOffset(8)] public uint Tail;

    
    [FieldOffset(0)] public int Recog;
    [FieldOffset(4)] public ushort Ident;
    [FieldOffset(6)] public ushort Param;
    [FieldOffset(8)] public ushort Tag;
    [FieldOffset(10)] public ushort Series;

    public static CmdPack MakeDefaultMsg(ushort ident, int recog, ushort param, ushort tag, ushort series) =>
        new()
        {
            Recog = recog,
            Ident = ident,
            Param = param,
            Tag = tag,
            Series = series
        };
}

