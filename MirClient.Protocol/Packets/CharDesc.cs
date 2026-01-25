using System.Runtime.InteropServices;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct CharDesc
{
    public int Feature;
    public int Status;
    public int StatusEx;
}
