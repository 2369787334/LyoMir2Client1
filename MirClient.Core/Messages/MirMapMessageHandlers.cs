using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirMapMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        Action<string, string, int, int, int> onMapChange,
        Action<int, ShortMessage?, int, int, int>? onShowEvent = null,
        Action<int>? onHideEvent = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(onMapChange);

        dispatcher.Register(Grobal2.SM_SHOWEVENT, packet =>
        {
            ShortMessage? msg = null;
            if (EdCode.TryDecodeBuffer(packet.BodyEncoded, out ShortMessage decoded))
            {
                msg = decoded;
                int x = packet.Header.Tag & 0xFFFF;
                int y = packet.Header.Series;
                int type = packet.Header.Param;
                log?.Invoke($"[event] SM_SHOWEVENT id={packet.Header.Recog} x={x} y={y} type={type} ident={msg.Value.Ident} msg={msg.Value.Message}");
                onShowEvent?.Invoke(packet.Header.Recog, msg, x, y, type);
            }
            else
            {
                log?.Invoke($"[event] SM_SHOWEVENT decode failed (bodyLen={packet.BodyEncoded.Length})");
            }

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HIDEEVENT, packet =>
        {
            log?.Invoke($"[event] SM_HIDEEVENT id={packet.Header.Recog}");
            onHideEvent?.Invoke(packet.Header.Recog);
            return ValueTask.CompletedTask;
        });

        void HandleMapChange(string identName, MirServerPacket packet)
        {
            string mapName = EdCode.DecodeString(packet.BodyEncoded);
            int x = packet.Header.Param;
            int y = packet.Header.Tag;
            int light = packet.Header.Series;
            onMapChange(identName, mapName, x, y, light);
        }

        dispatcher.Register(Grobal2.SM_NEWMAP, packet =>
        {
            HandleMapChange("SM_NEWMAP", packet);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CHANGEMAP, packet =>
        {
            HandleMapChange("SM_CHANGEMAP", packet);
            return ValueTask.CompletedTask;
        });
    }
}
