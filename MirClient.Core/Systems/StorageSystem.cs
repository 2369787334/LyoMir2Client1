using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public sealed class StorageSystem
{
    private const long ActionCooldownMs = 120;

    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    private long _lastActionMs;

    public StorageSystem(MirClientSession session, MirWorldState world, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public async ValueTask TryTakeBackAsync(ClientItem item, int storageIndex, CancellationToken token)
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
            _log?.Invoke("[storage] takeback ignored: merchant not set");
            return;
        }

        ushort lo = unchecked((ushort)(item.MakeIndex & 0xFFFF));
        ushort hi = unchecked((ushort)((item.MakeIndex >> 16) & 0xFFFF));
        ushort count = item.S.Overlap > 0 ? item.Dura : (ushort)0;

        try
        {
            await _session.SendClientStringAsync(
                Grobal2.CM_USERTAKEBACKSTORAGEITEM,
                merchantId,
                lo,
                hi,
                count,
                item.NameString,
                token);
            _log?.Invoke($"[storage] CM_USERTAKEBACKSTORAGEITEM '{item.NameString}' makeIndex={item.MakeIndex} count={count} idx={storageIndex}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[storage] CM_USERTAKEBACKSTORAGEITEM send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async ValueTask TryStoreAsync(ClientItem item, int bagSlotIndex, CancellationToken token)
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
            _log?.Invoke("[storage] store ignored: merchant not set");
            return;
        }

        ushort lo = unchecked((ushort)(item.MakeIndex & 0xFFFF));
        ushort hi = unchecked((ushort)((item.MakeIndex >> 16) & 0xFFFF));
        ushort count = item.Dura;

        try
        {
            await _session.SendClientStringAsync(
                Grobal2.CM_USERSTORAGEITEM,
                merchantId,
                lo,
                hi,
                count,
                item.NameString,
                token);
            _log?.Invoke($"[storage] CM_USERSTORAGEITEM '{item.NameString}' makeIndex={item.MakeIndex} count={count} slot={bagSlotIndex}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[storage] CM_USERSTORAGEITEM send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
