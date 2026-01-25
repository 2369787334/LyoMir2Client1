using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Messages;

public static class MirDoorMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<int, int>? onOpenDoor = null,
        Action<int, int>? onCloseDoor = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_OPENDOOR_OK, packet =>
        {
            if (!world.MapMoving)
                onOpenDoor?.Invoke(packet.Header.Param, packet.Header.Tag);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_OPENDOOR_LOCK, _ =>
        {
            log?.Invoke("[door] locked");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CLOSEDOOR, packet =>
        {
            if (!world.MapMoving)
                onCloseDoor?.Invoke(packet.Header.Param, packet.Header.Tag);
            return ValueTask.CompletedTask;
        });
    }
}

