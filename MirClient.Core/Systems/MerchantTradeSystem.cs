using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public sealed class MerchantTradeSystem
{
    private const long ActionCooldownMs = 120;

    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    private long _lastActionMs;

    public MerchantTradeSystem(MirClientSession session, MirWorldState world, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public async ValueTask TrySellAsync(ClientItem item, int bagSlotIndex, CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        long nowMs = Environment.TickCount64;
        if (nowMs - _lastActionMs < ActionCooldownMs)
            return;

        _lastActionMs = nowMs;

        int merchantId = _world.CurrentMerchantId;
        if (merchantId <= 0)
        {
            _log?.Invoke("[merchant] trade ignored: merchant not set");
            return;
        }

        ushort lo = unchecked((ushort)(item.MakeIndex & 0xFFFF));
        ushort hi = unchecked((ushort)((item.MakeIndex >> 16) & 0xFFFF));
        ushort count = item.S.Overlap > 0 ? item.Dura : (ushort)0;

        try
        {
            await _session.SendClientStringAsync(
                Grobal2.CM_USERSELLITEM,
                merchantId,
                lo,
                hi,
                count,
                item.NameString,
                token);
            _log?.Invoke($"[merchant] CM_USERSELLITEM '{item.NameString}' makeIndex={item.MakeIndex} count={count} slot={bagSlotIndex}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[merchant] CM_USERSELLITEM send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async ValueTask TryRepairAsync(ClientItem item, int bagSlotIndex, CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        long nowMs = Environment.TickCount64;
        if (nowMs - _lastActionMs < ActionCooldownMs)
            return;

        _lastActionMs = nowMs;

        int merchantId = _world.CurrentMerchantId;
        if (merchantId <= 0)
        {
            _log?.Invoke("[merchant] trade ignored: merchant not set");
            return;
        }

        ushort lo = unchecked((ushort)(item.MakeIndex & 0xFFFF));
        ushort hi = unchecked((ushort)((item.MakeIndex >> 16) & 0xFFFF));

        try
        {
            await _session.SendClientStringAsync(
                Grobal2.CM_USERREPAIRITEM,
                merchantId,
                lo,
                hi,
                0,
                item.NameString,
                token);
            _log?.Invoke($"[merchant] CM_USERREPAIRITEM '{item.NameString}' makeIndex={item.MakeIndex} slot={bagSlotIndex}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[merchant] CM_USERREPAIRITEM send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

