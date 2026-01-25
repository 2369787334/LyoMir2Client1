using System.Runtime.InteropServices;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MessageBodyW
{
    public ushort Param1;
    public ushort Param2;
    public ushort Tag1;
    public ushort Tag2;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct MessageBodyWL
{
    public int Param1;
    public int Param2;
    public int Tag1;
    public int Tag2;
}
