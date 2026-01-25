using MirClient.Core.Util;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;

namespace MirClient.Core.Messages;

public static class MirInternalPowerMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string, MirColor4>? addChatLine = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_INPOWERINFO, packet =>
        {
            string text = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
                addChatLine?.Invoke($"[ip] {text}", new MirColor4(0.92f, 0.92f, 0.92f, 1f));

            log?.Invoke($"[ip] SM_INPOWERINFO recog={packet.Header.Recog} param={packet.Header.Param} tag={packet.Header.Tag} series={packet.Header.Series} '{text}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_INTERNALPOWER, packet =>
        {
            world.ApplyInternalPower(packet.Header.Recog, packet.Header.Param);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROINTERNALPOWER, packet =>
        {
            world.ApplyInternalPower(packet.Header.Recog, packet.Header.Param);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_INTERNALPOWER2, packet =>
        {
            world.ApplyInternalPower(packet.Header.Recog, packet.Header.Param);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROINTERNALPOWER2, packet =>
        {
            world.ApplyInternalPower(packet.Header.Recog, packet.Header.Param);
            return ValueTask.CompletedTask;
        });
    }
}

