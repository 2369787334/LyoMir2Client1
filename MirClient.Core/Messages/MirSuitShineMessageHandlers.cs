using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Messages;

public static class MirSuitShineMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_SUITESTR, packet =>
        {
            int compressedLen = packet.Header.Recog;
            if (world.TryApplySuiteStrs(compressedLen, packet.BodyEncoded, out int suiteCount))
            {
                log?.Invoke($"[suite] SM_SUITESTR bytes={compressedLen} items={suiteCount}");
            }
            else
            {
                log?.Invoke($"[suite] SM_SUITESTR decode failed (bytes={compressedLen} bodyLen={packet.BodyEncoded.Length}).");
            }

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_Allshine, packet =>
        {
            int byteLen = packet.Header.Recog;
            if (world.TryApplyAllshine(byteLen, packet.BodyEncoded))
            {
                log?.Invoke($"[shine] SM_Allshine bytes={byteLen}");
            }
            else
            {
                log?.Invoke($"[shine] SM_Allshine decode failed (bytes={byteLen} bodyLen={packet.BodyEncoded.Length}).");
            }

            return ValueTask.CompletedTask;
        });
    }
}

