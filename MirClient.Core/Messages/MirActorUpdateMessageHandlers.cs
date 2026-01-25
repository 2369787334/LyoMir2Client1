using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;

namespace MirClient.Core.Messages;

public static class MirActorUpdateMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Func<long>? getTickMs = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        getTickMs ??= () => Environment.TickCount64;

        dispatcher.Register(Grobal2.SM_FEATURECHANGED, packet =>
        {
            int recogId = packet.Header.Recog;
            int feature = (packet.Header.Tag << 16) | packet.Header.Param;
            int featureEx = packet.Header.Series;
            world.TryApplyActorFeatureChanged(recogId, feature, featureEx, packet.BodyEncoded);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CHARSTATUSCHANGED, packet =>
        {
            int recogId = packet.Header.Recog;
            int state = (packet.Header.Tag << 16) | packet.Header.Param;
            ushort hitSpeed = packet.Header.Series;
            string text = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;

            if (world.TryApplyActorCharStatusChanged(recogId, state, hitSpeed) && text == "1")
            {
                long nowMs = getTickMs();
                world.TryAddStruckEffect(recogId, 1110, 0, nowMs);
            }

            return ValueTask.CompletedTask;
        });
    }
}

