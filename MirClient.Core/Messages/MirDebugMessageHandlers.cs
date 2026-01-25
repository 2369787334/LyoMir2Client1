using System.Diagnostics;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirDebugMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Func<int>? incrementTestReceiveCount = null,
        Func<long>? getTimestamp = null,
        Func<long>? getTickMs = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        Func<long> nowTimestamp = getTimestamp ?? Stopwatch.GetTimestamp;
        Func<long> nowMs = getTickMs ?? (() => Environment.TickCount64);

        dispatcher.Register(Grobal2.SM_BUTCH, packet =>
        {
            
            int recogId = packet.Header.Recog;
            if (recogId == 0)
                return ValueTask.CompletedTask;

            if (world.MapMoving)
                return ValueTask.CompletedTask;

            if (world.MyselfRecogIdSet && recogId == world.MyselfRecogId)
                return ValueTask.CompletedTask;

            if (!world.TryGetActor(recogId, out _))
                return ValueTask.CompletedTask;

            if (!EdCode.TryDecodeBuffer(packet.BodyEncoded, out MessageBodyWL wl))
            {
                log?.Invoke($"[dig] SM_BUTCH decode failed (bodyLen={packet.BodyEncoded.Length})");
                return ValueTask.CompletedTask;
            }

            var desc = new CharDesc { Feature = wl.Param1, Status = wl.Param2, StatusEx = 0 };

            _ = world.TryApplyActorAction(
                Grobal2.SM_SITDOWN,
                recogId,
                packet.Header.Param,
                packet.Header.Tag,
                packet.Header.Series,
                desc.Feature,
                desc.Status,
                userName: null,
                descUserName: null,
                nameColor: null,
                nameOffset: null,
                nowTimestamp(),
                nowMs(),
                out _,
                out _);

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_TEST, packet =>
        {
            if (incrementTestReceiveCount == null)
            {
                log?.Invoke($"[msg] SM_TEST bodyLen={packet.BodyEncoded.Length}");
                return ValueTask.CompletedTask;
            }

            int count = incrementTestReceiveCount();
            if (count <= 3 || count % 100 == 0)
                log?.Invoke($"[msg] SM_TEST count={count} bodyLen={packet.BodyEncoded.Length}");

            return ValueTask.CompletedTask;
        });
    }
}
