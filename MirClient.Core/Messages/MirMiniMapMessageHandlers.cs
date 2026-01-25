using MirClient.Core.Util;
using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Messages;

public static class MirMiniMapMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string, MirColor4>? addChatLine = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_READMINIMAP_OK, packet =>
        {
            world.ApplyReadMiniMapOk(packet.Header.Param);
            log?.Invoke($"[minimap] SM_READMINIMAP_OK visible={(world.MiniMapVisible ? 1 : 0)} index={world.MiniMapIndex} param={packet.Header.Param}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_READMINIMAP_FAIL, _ =>
        {
            world.ApplyReadMiniMapFail();
            addChatLine?.Invoke("没有小地图", new MirColor4(1.0f, 0.35f, 0.35f, 1f));
            log?.Invoke("[minimap] SM_READMINIMAP_FAIL");
            return ValueTask.CompletedTask;
        });
    }
}

