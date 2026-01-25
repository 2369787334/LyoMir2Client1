using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Messages;

public static class MirEffectMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string, bool>? playSoundFile = null,
        Action<int, int, int>? addNormalEffect = null,
        Func<long>? getTickMs = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        Func<long> tickMs = getTickMs ?? (() => Environment.TickCount64);

        dispatcher.Register(Grobal2.SM_HEROSPELL, packet =>
        {
            int actorId = packet.Header.Recog;
            int param = packet.Header.Param;
            int tag = packet.Header.Tag;

            if (world.Actors.ContainsKey(actorId))
                playSoundFile?.Invoke(@"Wav\splitshadow.wav", false);

            log?.Invoke($"[fx] SM_HEROSPELL actor={actorId} param={param} tag={tag}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SQUAREPOWERUP, packet =>
        {
            int max = packet.Header.Recog;
            int current = packet.Header.Param;
            world.ApplySquarePowerUp(current, max);
            log?.Invoke($"[square] SM_SQUAREPOWERUP hp={world.MySquHitPoint}/{world.MyMaxSquHitPoint}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_STRUCKEFFECT, packet =>
        {
            int nad = 0;
            _ = int.TryParse(packet.BodyEncoded.AsSpan().Trim(), out nad);
            int tag = nad > 0 && nad > packet.Header.Tag ? nad : packet.Header.Tag;

            if (world.MapMoving)
                return ValueTask.CompletedTask;

            int targetId = packet.Header.Recog;
            int effectId = packet.Header.Param;
            long nowMs = tickMs();

            if (effectId == 29)
            {
                world.TryApplyActorLastDamage(targetId, tag, nowMs);
                return ValueTask.CompletedTask;
            }

            if (effectId == 0)
                return ValueTask.CompletedTask;

            if (!world.TryAddStruckEffect(targetId, effectId, tag, nowMs))
                return ValueTask.CompletedTask;

            string? file = effectId switch
            {
                9 or 31 => @"Wav\dare-death.wav",
                10 or 32 => @"Wav\dare-win.wav",
                11 or 15 or 16 => @"Wav\hero-shield.wav",
                13 => @"Wav\UnionHitShield.wav",
                14 => @"Wav\powerup.wav",
                18 or 19 or 20 => @"Wav\warpower-up.wav",
                >= 33 and <= 40 => @"Wav\SelectBoxFlash.wav",
                >= 41 and <= 43 => @"Wav\Flashbox.wav",
                _ => null
            };

            if (file != null)
                playSoundFile?.Invoke(file, false);

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_FIREWORKS, packet =>
        {
            
            int type = packet.Header.Param;
            int x = packet.Header.Tag;
            int y = packet.Header.Series;

            if (!world.MapMoving && type != 0)
                addNormalEffect?.Invoke(type, x, y);

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_NORMALEFFECT, packet =>
        {
            int type = packet.Header.Series;
            int x = packet.Header.Param;
            int y = packet.Header.Tag;

            if (!world.MapMoving && type != 0)
                addNormalEffect?.Invoke(type, x, y);

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_LOOPNORMALEFFECT, packet =>
        {
            if (world.MapMoving)
                return ValueTask.CompletedTask;

            int type = packet.Header.Series;
            int x = packet.Header.Param;
            int y = packet.Header.Tag;

            if (type != 0)
                world.UpsertLoopNormalEffect(type, x, y, tickMs());

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_LOOPSCREENEFFECT, packet =>
        {
            int type = packet.Header.Series;
            int x = packet.Header.Param;
            int y = packet.Header.Tag;

            if (type != 0)
                world.UpsertLoopScreenEffect(type, x, y, tickMs());

            return ValueTask.CompletedTask;
        });
    }
}
