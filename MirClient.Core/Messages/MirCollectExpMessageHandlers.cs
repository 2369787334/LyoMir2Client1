using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Messages;

public static class MirCollectExpMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_COLLECTEXP, packet =>
        {
            uint exp = unchecked((uint)packet.Header.Recog);
            uint ipExp = (uint)(((uint)packet.Header.Tag << 16) | packet.Header.Param);
            world.ApplyCollectExp(exp, ipExp);
            log?.Invoke($"[collect] SM_COLLECTEXP exp={exp} ipExp={ipExp}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_COLLECTEXPSTATE, packet =>
        {
            byte level = packet.Header.Series > byte.MaxValue ? byte.MaxValue : (byte)packet.Header.Series;
            uint expMax = unchecked((uint)packet.Header.Recog);
            uint ipExpMax = (uint)(((uint)packet.Header.Tag << 16) | packet.Header.Param);
            world.ApplyCollectExpState(level, expMax, ipExpMax);
            log?.Invoke($"[collect] SM_COLLECTEXPSTATE lv={level} expMax={expMax} ipExpMax={ipExpMax}");
            return ValueTask.CompletedTask;
        });
    }
}

