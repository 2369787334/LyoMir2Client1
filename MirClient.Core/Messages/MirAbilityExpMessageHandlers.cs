using MirClient.Core.Util;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirAbilityExpMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string, MirColor4>? addChatLine = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_ABILITY, packet =>
        {
            int gold = packet.Header.Recog;
            byte job = (byte)(packet.Header.Param & 0xFF);
            byte iPowerLevel = (byte)((packet.Header.Param >> 8) & 0xFF);
            uint gameGold = ((uint)packet.Header.Series << 16) | packet.Header.Tag;

            if (EdCode.TryDecodeBuffer(packet.BodyEncoded, out Ability ability))
            {
                world.ApplyAbility(gold, job, iPowerLevel, gameGold, ability);
            }
            else if (EdCode.TryDecodeBuffer(packet.BodyEncoded, out OldAbility oldAbility))
            {
                world.ApplyOldAbility(gold, job, iPowerLevel, gameGold, oldAbility);
            }
            else
            {
                log?.Invoke($"[abil] SM_ABILITY decode failed (len={packet.BodyEncoded.Length}).");
            }

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DAYCHANGING, packet =>
        {
            int dayBright = packet.Header.Param;
            int darkLevel = packet.Header.Tag;
            world.ApplyDayChanging(dayBright, darkLevel);
            log?.Invoke($"[env] SM_DAYCHANGING bright={dayBright} dark={world.DarkLevel} viewFog={world.ViewFog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_WINEXP, packet =>
        {
            int totalExp = packet.Header.Recog;
            uint gained = ((uint)packet.Header.Tag << 16) | packet.Header.Param;
            world.ApplyWinExp(totalExp);

            if (gained > 0)
                addChatLine?.Invoke($"经验值 +{gained}", new MirColor4(1.0f, 0.35f, 0.35f, 1f));

            log?.Invoke($"[exp] SM_WINEXP total={totalExp} gain={gained}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROWINEXP, packet =>
        {
            int totalExp = packet.Header.Recog;
            uint gained = ((uint)packet.Header.Tag << 16) | packet.Header.Param;
            world.ApplyHeroWinExp(totalExp);

            if (gained > 0)
                addChatLine?.Invoke($"(英雄)经验值 +{gained}", new MirColor4(1.0f, 0.35f, 0.35f, 1f));

            log?.Invoke($"[exp] SM_HEROWINEXP total={totalExp} gain={gained}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_LEVELUP, packet =>
        {
            int level = packet.Header.Param;
            world.ApplyLevelUp(level);
            addChatLine?.Invoke("您的等级已升级！", new MirColor4(0.55f, 0.95f, 0.55f, 1f));
            log?.Invoke($"[exp] SM_LEVELUP level={level}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROLEVELUP, packet =>
        {
            int level = packet.Header.Param;
            world.ApplyHeroLevelUp(level);
            addChatLine?.Invoke("(英雄)等级已升级！", new MirColor4(0.55f, 0.95f, 0.55f, 1f));
            log?.Invoke($"[exp] SM_HEROLEVELUP level={level}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_WINIPEXP, packet =>
        {
            int totalExp = packet.Header.Recog;
            uint gained = ((uint)packet.Header.Tag << 16) | packet.Header.Param;
            ushort magicRange = packet.Header.Series;

            world.ApplyWinIpExp(totalExp, magicRange);

            if (gained > 0)
                addChatLine?.Invoke($"{gained}点内功经验增加", new MirColor4(1.0f, 0.35f, 0.35f, 1f));

            log?.Invoke($"[ip-exp] SM_WINIPEXP total={totalExp} gain={gained} range={magicRange}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROWINIPEXP, packet =>
        {
            int totalExp = packet.Header.Recog;
            uint gained = ((uint)packet.Header.Tag << 16) | packet.Header.Param;

            world.ApplyHeroWinIpExp(totalExp);

            if (gained > 0)
                addChatLine?.Invoke($"(英雄){gained}点内功经验增加", new MirColor4(1.0f, 0.35f, 0.35f, 1f));

            log?.Invoke($"[ip-exp] SM_HEROWINIPEXP total={totalExp} gain={gained}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_WINNIMBUSEXP, packet =>
        {
            int value = packet.Header.Recog;
            world.ApplyWinNimbusExp(value);

            if (value > 0)
                addChatLine?.Invoke($"当前灵气值 {value}", new MirColor4(0.92f, 0.92f, 0.92f, 1f));

            log?.Invoke($"[nimbus] SM_WINNIMBUSEXP value={value}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROWINNIMBUSEXP, packet =>
        {
            int value = packet.Header.Recog;
            world.ApplyHeroWinNimbusExp(value);

            if (value > 0)
                addChatLine?.Invoke($"(英雄)当前灵气值 {value}", new MirColor4(0.92f, 0.92f, 0.92f, 1f));

            log?.Invoke($"[nimbus] SM_HEROWINNIMBUSEXP value={value}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SUBABILITY, packet =>
        {
            world.ApplySubAbility(packet.Header.Recog, packet.Header.Param, packet.Header.Tag, packet.Header.Series);
            log?.Invoke($"[abil] SM_SUBABILITY hit={world.MyHitPoint} speed={world.MySpeedPoint} antiMagic={world.MyAntiMagic} addDmg={world.MyAddDamage} decDmg={world.MyDecDamage}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_REFDIAMOND, packet =>
        {
            int diamond = packet.Header.Recog;
            int gird = packet.Header.Param;
            world.ApplyRefDiamond(diamond, gird);
            log?.Invoke($"[abil] SM_REFDIAMOND diamond={diamond} gird={gird}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEALTHSPELLCHANGED, packet =>
        {
            world.ApplyHealthSpellChanged(packet.Header.Recog, packet.Header.Param, packet.Header.Tag, packet.Header.Series);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_WEIGHTCHANGED, packet =>
        {
            int weight = packet.Header.Recog;
            int wearWeight = packet.Header.Param;
            int handWeight = packet.Header.Tag;
            world.ApplyWeightChanged(weight, wearWeight, handWeight);
            log?.Invoke($"[weight] W {weight}/{wearWeight}/{handWeight}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GOLDCHANGED, packet =>
        {
            int gold = packet.Header.Recog;
            ushort gameGoldLo = packet.Header.Param;
            ushort gameGoldHi = packet.Header.Tag;
            uint gameGold = ((uint)gameGoldHi << 16) | gameGoldLo;

            world.ApplyGoldChanged(gold, gameGold);
            log?.Invoke($"[gold] Gold={gold} GameGold={gameGold}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GAMEGOLDNAME, packet =>
        {
            int gold = packet.Header.Recog;
            int point = ((packet.Header.Tag & 0xFFFF) << 16) | (packet.Header.Param & 0xFFFF);
            string body = EdCode.DecodeString(packet.BodyEncoded);

            world.ApplyGameGoldName(gold, point, body);

            if (!string.IsNullOrEmpty(world.GameGoldName) || !string.IsNullOrEmpty(world.GamePointName))
                log?.Invoke($"[gold] {world.GameGoldName}={world.GameGold} {world.GamePointName}={world.GamePoint}");

            return ValueTask.CompletedTask;
        });
    }
}

