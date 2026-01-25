using MirClient.Core.Util;
using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Messages;

public static class MirAttackModeMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string, MirColor4>? addChatLine = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_ATTACKMODE, packet =>
        {
            int mode = packet.Header.Param;
            if (mode == 0 &&
                packet.Header.Tag == 0 &&
                packet.Header.Series == 0 &&
                packet.BodyEncoded.Length == 0 &&
                packet.Header.Recog is >= Grobal2.HAM_ALL and <= Grobal2.HAM_PKATTACK)
            {
                mode = packet.Header.Recog;
            }

            world.ApplyAttackMode(mode);
            addChatLine?.Invoke($"[mode] AttackMode={world.AttackModeLabel}", new MirColor4(0.92f, 0.92f, 0.92f, 1f));
            log?.Invoke($"[mode] SM_ATTACKMODE mode={world.AttackMode}({world.AttackModeLabel})");
            return ValueTask.CompletedTask;
        });
    }
}

