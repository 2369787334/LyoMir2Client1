using System.Diagnostics;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirChangeFaceMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Func<long>? getTimestamp = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        getTimestamp ??= () => Stopwatch.GetTimestamp();

        dispatcher.Register(Grobal2.SM_CHANGEFACE, packet =>
        {
            int recogId = packet.Header.Recog;
            int waitForRecogId = (packet.Header.Tag << 16) | packet.Header.Param;

            CharDesc desc = default;
            if (!EdCode.TryDecodeBuffer(packet.BodyEncoded, out desc) && EdCode.TryDecodeBuffer(packet.BodyEncoded, out CharDesc2 desc2))
                desc = new CharDesc { Feature = desc2.Feature, Status = desc2.Status, StatusEx = 0 };

            if (world.TryQueueChangeFace(recogId, waitForRecogId, desc.Feature, desc.Status, getTimestamp()))
                log?.Invoke($"[actor] SM_CHANGEFACE queued {recogId}->{waitForRecogId} feature={desc.Feature} status={desc.Status}");
            else
                log?.Invoke($"[actor] SM_CHANGEFACE ignored recog={recogId} waitFor={waitForRecogId} feature={desc.Feature} status={desc.Status}");

            return ValueTask.CompletedTask;
        });
    }
}

