using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public struct StallSlot
{
    public ClientItem Item;
    public int Price;
    public byte GoldType;

    public bool HasItem => Item.MakeIndex != 0;
}

