using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public readonly record struct MirServerPacket(string RawPayload, CmdPack Header, string BodyEncoded);

