using System.Runtime.InteropServices;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ShortMessage
{
    public ushort Ident;
    public ushort Message;
}

