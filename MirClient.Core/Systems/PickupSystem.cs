using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class PickupSystem
{
    private readonly MirClientSession _session;
    private readonly CommandThrottleSystem _throttle;
    private readonly Action<string>? _log;

    public PickupSystem(MirClientSession session, CommandThrottleSystem throttle, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _throttle = throttle ?? throw new ArgumentNullException(nameof(throttle));
        _log = log;
    }

    public async ValueTask TryPickupAsync(int mapX, int mapY, CancellationToken token)
    {
        if (!_throttle.TryPickupSend())
            return;

        try
        {
            await _session.SendClientMessageAsync(
                Grobal2.CM_PICKUP,
                recog: 0,
                param: unchecked((ushort)mapX),
                tag: unchecked((ushort)mapY),
                series: 0,
                token);
            _log?.Invoke($"[bag] CM_PICKUP x={mapX} y={mapY}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[bag] CM_PICKUP send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

