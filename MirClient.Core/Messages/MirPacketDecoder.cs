using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirPacketDecoder
{
    public static bool TryDecode(string rawPayload, out MirServerPacket packet)
    {
        packet = default;
        if (string.IsNullOrEmpty(rawPayload))
            return false;

        
        
        ReadOnlySpan<char> span = rawPayload.AsSpan();

        if (span[0] is >= '0' and <= '9')
            span = span[1..];

        if (span.Length == 0)
            return false;

        if (span[0] == '+')
        {
            CmdPack internalHeader = CmdPack.MakeDefaultMsg(MirInternalIdents.ActMessage, recog: 0, param: 0, tag: 0, series: 0);
            packet = new MirServerPacket(rawPayload, internalHeader, span.ToString());
            return true;
        }

        if (span.Length < Grobal2.DEFBLOCKSIZE)
            return false;

        string headerEncoded = span[..Grobal2.DEFBLOCKSIZE].ToString();
        string bodyEncoded = span[Grobal2.DEFBLOCKSIZE..].ToString();

        CmdPack header = EdCode.DecodeMessage(headerEncoded);
        if (header.Ident == 0)
            return false;

        packet = new MirServerPacket(rawPayload, header, bodyEncoded);
        return true;
    }
}
