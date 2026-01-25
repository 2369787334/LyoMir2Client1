using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Messages;

public static class MirServerConfigMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_SERVERCONFIG, packet =>
        {
            if (!world.TryApplyServerConfig(packet.Header.Recog, packet.Header.Series, packet.BodyEncoded))
            {
                log?.Invoke($"[config] SM_SERVERCONFIG decode failed (bodyLen={packet.BodyEncoded.Length})");
                return ValueTask.CompletedTask;
            }

            log?.Invoke($"[config] SM_SERVERCONFIG openAutoPlay={world.OpenAutoPlay} runH={world.CanRunHuman} runM={world.CanRunMon} runN={world.CanRunNpc} warRunAll={world.CanRunAllInWarZone}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SERVERCONFIG_LEGACY, packet =>
        {
            if (!world.TryApplyServerConfig(packet.Header.Recog, packet.Header.Series, packet.BodyEncoded))
            {
                log?.Invoke($"[config] SM_SERVERCONFIG_LEGACY decode failed (bodyLen={packet.BodyEncoded.Length})");
                return ValueTask.CompletedTask;
            }

            log?.Invoke($"[config] SM_SERVERCONFIG_LEGACY openAutoPlay={world.OpenAutoPlay} runH={world.CanRunHuman} runM={world.CanRunMon} runN={world.CanRunNpc} warRunAll={world.CanRunAllInWarZone}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SERVERCONFIG2, packet =>
        {
            if (!world.TryApplyServerConfig2(packet.Header.Param, packet.Header.Tag, packet.Header.Series, packet.BodyEncoded))
            {
                log?.Invoke($"[config] SM_SERVERCONFIG2 decode failed (bodyLen={packet.BodyEncoded.Length})");
                return ValueTask.CompletedTask;
            }

            log?.Invoke($"[config] SM_SERVERCONFIG2 autoSay={world.AutoSay} hero={world.HeroEnabled} mutiHero={world.MultiHero} stall={world.OpenStallSystem} eatInv={world.EatItemInvTime}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SERVERCONFIG3, packet =>
        {
            world.ApplyServerConfig3(packet.Header.Param, packet.Header.Tag, packet.Header.Series);
            log?.Invoke($"[config] SM_SERVERCONFIG3 speedRate={world.SpeedRateEnabled} hit={world.HitSpeedRate} mag={world.MagSpeedRate} move={world.MoveSpeedRate}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_RUNHUMAN, packet =>
        {
            bool canRunHuman = packet.Header.Recog != 0;
            world.ApplyRunHuman(canRunHuman);
            log?.Invoke($"[config] SM_RUNHUMAN canRun={canRunHuman}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_INSAFEZONEFLAG, packet =>
        {
            bool canRunSafeZone = packet.Header.Recog != 0;
            world.ApplyRunSafeZone(canRunSafeZone);
            log?.Invoke($"[config] SM_INSAFEZONEFLAG canRunSafeZone={canRunSafeZone}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_TIMECHECK_MSG, packet =>
        {
            log?.Invoke($"[anti] SM_TIMECHECK_MSG recog={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_AREASTATE, packet =>
        {
            world.ApplyAreaState(packet.Header.Recog);
            log?.Invoke($"[area] SM_AREASTATE value={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_ADJUST_BONUS, packet =>
        {
            if (world.TryApplyAdjustBonus(packet.Header.Recog, packet.BodyEncoded))
                log?.Invoke($"[bonus] SM_ADJUST_BONUS point={world.BonusPoint}");
            else
                log?.Invoke($"[bonus] SM_ADJUST_BONUS decode failed (bodyLen={packet.BodyEncoded.Length})");

            return ValueTask.CompletedTask;
        });
    }
}

