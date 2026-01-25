using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirMapConfigMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<int, string>? onMapDescription = null,
        Action<bool, bool>? onPlayerConfig = null,
        Action? onPlayerConfigTooFast = null,
        Action<int, int, int, int, string>? onItemShow = null,
        Action<int>? onItemHide = null,
        Func<bool>? isMapMoving = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_MAPDESCRIPTION, packet =>
        {
            int musicId = packet.Header.Recog;
            string title = EdCode.DecodeString(packet.BodyEncoded);
            onMapDescription?.Invoke(musicId, title);
            log?.Invoke($"[map] SM_MAPDESCRIPTION music={musicId} title='{title}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_PLAYERCONFIG, packet =>
        {
            if (packet.Header.Recog == -1)
            {
                onPlayerConfigTooFast?.Invoke();
                log?.Invoke("[config] SM_PLAYERCONFIG too fast");
                return ValueTask.CompletedTask;
            }

            bool hero = packet.Header.Tag != 0;
            bool showFashion = packet.Header.Series != 0;
            world.ApplyPlayerConfig(hero, showFashion);
            onPlayerConfig?.Invoke(hero, showFashion);
            log?.Invoke($"[config] SM_PLAYERCONFIG hero={hero} showFashion={showFashion} recog={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_ITEMSHOW, packet =>
        {
            int id = packet.Header.Recog;
            int x = packet.Header.Param;
            int y = packet.Header.Tag;
            int looks = packet.Header.Series;
            string name = EdCode.DecodeString(packet.BodyEncoded);

            if (isMapMoving == null || !isMapMoving())
                onItemShow?.Invoke(id, x, y, looks, name);

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_ITEMHIDE, packet =>
        {
            if (isMapMoving == null || !isMapMoving())
                onItemHide?.Invoke(packet.Header.Recog);
            return ValueTask.CompletedTask;
        });
    }
}
