using System.Runtime.InteropServices;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ClientStall
{
    public int MakeIndex;
    public int Price;
    public byte GoldType;
}

