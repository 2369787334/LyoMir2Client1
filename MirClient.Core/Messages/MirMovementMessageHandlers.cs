using System.Diagnostics;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirMovementMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Func<long>? getTimestamp = null,
        Func<long>? getTickCount64 = null,
        Action<string>? playSoundFile = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_POSTIONMOVE, packet =>
        {
            if (world.MapMoving)
                return ValueTask.CompletedTask;

            if (!EdCode.TryDecodeBuffer(packet.BodyEncoded, out PositionMoveMessage msg))
            {
                log?.Invoke($"[move] SM_POSTIONMOVE decode failed (len={packet.BodyEncoded.Length}).");
                return ValueTask.CompletedTask;
            }

            long ts = getTimestamp?.Invoke() ?? Stopwatch.GetTimestamp();
            long nowMs = getTickCount64?.Invoke() ?? Environment.TickCount64;

            world.TryApplyActorPositionMove(
                packet.Header.Recog,
                packet.Header.Param,
                packet.Header.Tag,
                packet.Header.Series,
                msg,
                ts,
                nowMs);

            if (world.MyselfRecogIdSet && packet.Header.Recog == world.MyselfRecogId)
                playSoundFile?.Invoke(@"Wav\cyclone.wav");

            return ValueTask.CompletedTask;
        });
    }
}
