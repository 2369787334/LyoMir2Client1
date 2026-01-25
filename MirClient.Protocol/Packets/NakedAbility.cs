using System.Runtime.InteropServices;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct NakedAbility
{
    public ushort DC;
    public ushort MC;
    public ushort SC;
    public ushort AC;
    public ushort MAC;
    public ushort HP;
    public ushort MP;
    public ushort Hit;
    public ushort Speed;
    public ushort X2;
}

