using System.Runtime.InteropServices;

namespace MirClient.Protocol.Packets;




[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ServerConfig
{
    public byte AutoSay;
    public fixed byte Reserved[30];
}
