using System.Diagnostics;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirActorStatusMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Func<bool> isMapMoving,
        Func<long>? getTimestamp = null,
        Func<long>? getTickMs = null,
        Action<ActorMarker, int>? onStruck = null,
        Action? onClearObjects = null,
        Action<string, int>? onHideActor = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(isMapMoving);

        getTimestamp ??= () => Stopwatch.GetTimestamp();
        getTickMs ??= () => Environment.TickCount64;

        dispatcher.Register(Grobal2.SM_SPELL, packet =>
        {
            if (isMapMoving())
                return ValueTask.CompletedTask;

            int who = packet.Header.Recog;
            int targetX = packet.Header.Param;
            int targetY = packet.Header.Tag;
            int effectNum = packet.Header.Series;
            string body = EdCode.DecodeString(packet.BodyEncoded);
            int magicId = 0;
            if (!string.IsNullOrWhiteSpace(body))
                int.TryParse(body.Trim(), out magicId);

            world.TryApplyActorSpell(who, targetX, targetY, effectNum, magicId, getTimestamp(), getTickMs());
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_MAGICFIRE, packet =>
        {
            if (isMapMoving())
                return ValueTask.CompletedTask;

            int who = packet.Header.Recog;
            int targetX = packet.Header.Param;
            int targetY = packet.Header.Tag;
            byte effType = (byte)(packet.Header.Series & 0xFF);
            byte effNum = (byte)((packet.Header.Series >> 8) & 0xFF);

            int targetRecogId = 0;
            int magFireLevel = 0;

            if (EdCode.TryDecodeBuffer(packet.BodyEncoded, out CharDesc target))
            {
                targetRecogId = target.Feature;
                magFireLevel = target.Status;
            }
            else if (EdCode.TryDecodeBuffer(packet.BodyEncoded, out int encodedTargetRecogId))
            {
                targetRecogId = encodedTargetRecogId;
            }
            else if (EdCode.TryDecodeBuffer(packet.BodyEncoded, out CharDesc2 target2))
            {
                targetRecogId = target2.Feature;
                magFireLevel = target2.Status;
            }

            world.TryApplyActorMagicFire(who, targetX, targetY, effType, effNum, targetRecogId, magFireLevel);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_MAGICFIRE_FAIL, packet =>
        {
            if (!isMapMoving())
                world.TryApplyActorMagicFireFail(packet.Header.Recog);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_STRUCK, packet =>
        {
            if (isMapMoving())
                return ValueTask.CompletedTask;

            int recogId = packet.Header.Recog;
            int hp = packet.Header.Param;
            int maxHp = packet.Header.Tag;
            int damage = packet.Header.Series;
            bool hasBody = EdCode.TryDecodeBuffer(packet.BodyEncoded, out MessageBodyWL wl);

            long now = getTimestamp();
            long nowMs = getTickMs();

            if (world.TryApplyActorStruck(recogId, hp, maxHp, damage, hasBody, wl, now, nowMs, out ActorMarker sfxActor) && damage > 0)
                onStruck?.Invoke(sfxActor, damage);

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CLEAROBJECTS, _ =>
        {
            log?.Invoke("[map] SM_CLEAROBJECTS");
            world.ApplyClearObjects();
            onClearObjects?.Invoke();
            return ValueTask.CompletedTask;
        });

        ValueTask HandleHide(string source, MirServerPacket packet)
        {
            if (isMapMoving())
                return ValueTask.CompletedTask;

            onHideActor?.Invoke(source, packet.Header.Recog);
            world.ApplyActorHide(packet.Header.Recog);
            return ValueTask.CompletedTask;
        }

        dispatcher.Register(Grobal2.SM_HIDE, packet => HandleHide("SM_HIDE", packet));
        dispatcher.Register(Grobal2.SM_DISAPPEAR, packet => HandleHide("SM_DISAPPEAR", packet));
        dispatcher.Register(Grobal2.SM_GHOST, packet => HandleHide("SM_GHOST", packet));
        dispatcher.Register(Grobal2.SM_SPACEMOVE_HIDE, packet => HandleHide("SM_SPACEMOVE_HIDE", packet));
        dispatcher.Register(Grobal2.SM_SPACEMOVE_HIDE2, packet => HandleHide("SM_SPACEMOVE_HIDE2", packet));
    }

}
