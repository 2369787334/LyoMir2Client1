using System.Runtime.InteropServices;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct PositionMoveMessage
{
    public uint Feature;
    public uint Status;
    public uint Hp;
    public uint MaxHp;
    public ushort CurrX;
    public ushort CurrY;
    public ushort Reserved;
    public ushort MagicId;
    public fixed byte Buff[256];
}

