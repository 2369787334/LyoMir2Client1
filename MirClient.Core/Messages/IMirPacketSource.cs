namespace MirClient.Core.Messages;

public interface IMirPacketSource
{
    bool TryDequeuePacket(out MirServerPacket packet);
}

