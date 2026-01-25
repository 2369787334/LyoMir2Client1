using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirHealthGaugeMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Func<long>? getTickMs = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        getTickMs ??= () => Environment.TickCount64;

        dispatcher.Register(Grobal2.SM_OPENHEALTH, packet =>
        {
            int actorId = packet.Header.Recog;

            int hp;
            int maxHp;
            if (EdCode.TryDecodeBuffer(packet.BodyEncoded, out ShortMessage shortMsg))
            {
                hp = (packet.Header.Tag << 16) | packet.Header.Param;
                maxHp = (shortMsg.Message << 16) | shortMsg.Ident;
            }
            else
            {
                hp = packet.Header.Param;
                maxHp = packet.Header.Tag;
            }

            world.ApplyOpenHealth(actorId, hp, maxHp);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CLOSEHEALTH, packet =>
        {
            world.ApplyCloseHealth(packet.Header.Recog);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_INSTANCEHEALGUAGE, packet =>
        {
            int actorId = packet.Header.Recog;

            int hp;
            int maxHp;
            if (EdCode.TryDecodeBuffer(packet.BodyEncoded, out ShortMessage shortMsg))
            {
                hp = (packet.Header.Tag << 16) | packet.Header.Param;
                maxHp = (shortMsg.Message << 16) | shortMsg.Ident;
            }
            else
            {
                hp = packet.Header.Param;
                maxHp = packet.Header.Tag;
            }

            world.ApplyInstanceHealthGauge(actorId, hp, maxHp, getTickMs());
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_BREAKWEAPON, packet =>
        {
            world.ApplyWeaponBreakEffect(packet.Header.Recog, getTickMs());
            log?.Invoke($"[fx] SM_BREAKWEAPON actor={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });
    }
}

