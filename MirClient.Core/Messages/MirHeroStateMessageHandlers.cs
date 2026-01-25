using System.Diagnostics;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirHeroStateMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Func<string, float?>? measureHalfNameWidth = null,
        Func<long>? getTimestamp = null,
        Func<long>? getTickMs = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        getTimestamp ??= () => Stopwatch.GetTimestamp();
        getTickMs ??= () => Environment.TickCount64;

        dispatcher.Register(Grobal2.SM_HEROLOGIN, packet =>
        {
            log?.Invoke($"[hero] SM_HEROLOGIN actor={packet.Header.Recog} x={packet.Header.Param} y={packet.Header.Tag}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROLOGOUT, packet =>
        {
            log?.Invoke($"[hero] SM_HEROLOGOUT x={packet.Header.Param} y={packet.Header.Tag}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROSTATE, packet =>
        {
            int heroId = packet.Header.Recog;
            int x = packet.Header.Param;
            int y = packet.Header.Tag;
            ushort dir = packet.Header.Series;

            if (!EdCode.TryDecodeBuffer(packet.BodyEncoded, out MessageBodyWL wl))
            {
                log?.Invoke($"[hero] SM_HEROSTATE decode failed (bodyLen={packet.BodyEncoded.Length})");
                return ValueTask.CompletedTask;
            }

            world.ApplyHeroState(heroId);

            if (!world.MapMoving)
            {
                _ = world.TryApplyActorAction(
                    Grobal2.SM_TURN,
                    heroId,
                    x,
                    y,
                    dir,
                    wl.Param1,
                    wl.Param2,
                    userName: null,
                    descUserName: null,
                    nameColor: null,
                    nameOffset: null,
                    getTimestamp(),
                    getTickMs(),
                    out _,
                    out _);
            }

            log?.Invoke($"[hero] SM_HEROSTATE id={heroId} x={x} y={y} feature={wl.Param1} status={wl.Param2}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROABILITY, packet =>
        {
            int gold = packet.Header.Recog;
            byte job = (byte)(packet.Header.Param & 0xFF);
            byte iPowerLevel = (byte)((packet.Header.Param >> 8) & 0xFF);
            ushort gloryPoint = packet.Header.Series;

            if (!EdCode.TryDecodeBuffer(packet.BodyEncoded, out Ability ability))
            {
                log?.Invoke($"[hero] SM_HEROABILITY decode failed (bodyLen={packet.BodyEncoded.Length})");
                return ValueTask.CompletedTask;
            }

            world.ApplyHeroAbility(gold, job, iPowerLevel, gloryPoint, ability);
            log?.Invoke($"[hero] SM_HEROABILITY gold={gold} job={job} iPowerLv={iPowerLevel} glory={gloryPoint} lv={ability.Level} hp={ability.HP}/{ability.MaxHP}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROSUBABILITY, packet =>
        {
            world.ApplyHeroSubAbility(packet.Header.Recog, (ushort)packet.Header.Param, (ushort)packet.Header.Tag, packet.Header.Series);
            log?.Invoke($"[hero] SM_HEROSUBABILITY hit={world.HeroHitPoint} speed={world.HeroSpeedPoint} antiMagic={world.HeroAntiMagic} addDmg={world.HeroAddDamage} decDmg={world.HeroDecDamage}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROSTATEDISPEAR, packet =>
        {
            world.ApplyHeroStateDisappear(packet.Header.Recog);
            log?.Invoke($"[hero] SM_HEROSTATEDISPEAR recog={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HERONAME, packet =>
        {
            if (!world.HeroActorIdSet)
                return ValueTask.CompletedTask;

            string raw = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;
            if (string.IsNullOrWhiteSpace(raw))
                return ValueTask.CompletedTask;

            byte nameColor = (byte)(packet.Header.Param & 0xFF);

            string userName = raw;
            string descUserName = string.Empty;
            int backslash = raw.IndexOf('\\');
            if (backslash >= 0)
            {
                userName = raw[..backslash];
                descUserName = backslash + 1 < raw.Length ? raw[(backslash + 1)..] : string.Empty;
            }

            float? nameOffset = null;
            if (measureHalfNameWidth != null && !string.IsNullOrEmpty(userName))
                nameOffset = measureHalfNameWidth(userName);

            world.TryApplyActorUserName(world.HeroActorId, userName, descUserName, nameColor, attribute: 0, nameOffset);

            log?.Invoke($"[hero] SM_HERONAME hero={world.HeroActorId} color={nameColor} raw='{raw}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROLOYALTY, packet =>
        {
            string raw = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;
            world.ApplyHeroLoyalty(raw);
            log?.Invoke($"[hero] SM_HEROLOYALTY '{world.HeroLoyalty}'");
            return ValueTask.CompletedTask;
        });
    }
}

