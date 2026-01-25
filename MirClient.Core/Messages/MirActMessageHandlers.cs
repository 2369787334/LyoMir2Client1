using MirClient.Core.Util;
using MirClient.Core.World;

namespace MirClient.Core.Messages;

public static class MirActMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Func<long>? getTickMs = null,
        Action<string, MirColor4>? addChatLine = null,
        Action<int>? playSfxById = null,
        Action<string>? playSoundFile = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        Func<long> tickMs = getTickMs ?? (() => Environment.TickCount64);
        var unhandledActMessages = new HashSet<string>(StringComparer.Ordinal);

        dispatcher.Register(MirInternalIdents.ActMessage, packet =>
        {
            string datablock = packet.BodyEncoded;
            long nowMs = tickMs();
            if (world.TryApplyActionMessage(datablock, nowMs, out string? chatLine, out ActMessageSideEffect sideEffect))
            {
                if (!string.IsNullOrWhiteSpace(chatLine))
                    addChatLine?.Invoke(chatLine, new MirColor4(0.92f, 0.92f, 0.92f, 1f));

                if (sideEffect.StruckEffectType != 0 && world.MyselfRecogIdSet && world.MyselfRecogId != 0)
                    world.TryAddStruckEffect(world.MyselfRecogId, sideEffect.StruckEffectType, tag: 0, startMs: nowMs);

                if (sideEffect.SfxId > 0)
                    playSfxById?.Invoke(sideEffect.SfxId);

                if (!string.IsNullOrWhiteSpace(sideEffect.SoundFile))
                    playSoundFile?.Invoke(sideEffect.SoundFile);

                return ValueTask.CompletedTask;
            }

            if (unhandledActMessages.Add(datablock))
                log?.Invoke($"[actmsg] unhandled: '{datablock}'");

            return ValueTask.CompletedTask;
        });
    }
}
