using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Messages;

public static class MirQueryValueMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<QueryValueRequest>? onPrompt = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_QUERYVALUE, packet =>
        {
            ushort param = packet.Header.Param;
            if (!world.TryApplyQueryValue(param, packet.BodyEncoded, out QueryValueRequest request))
            {
                log?.Invoke($"[queryval] SM_QUERYVALUE decode failed (bodyLen={packet.BodyEncoded.Length})");
                return ValueTask.CompletedTask;
            }

            log?.Invoke($"[queryval] SM_QUERYVALUE mode={request.Mode} icon={request.Icon} promptLen={request.Prompt.Length}");

            if (!world.QueryValuePending)
                return ValueTask.CompletedTask;

            onPrompt?.Invoke(request);
            return ValueTask.CompletedTask;
        });
    }
}

