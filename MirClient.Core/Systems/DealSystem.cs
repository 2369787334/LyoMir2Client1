using MirClient.Core.Messages;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public sealed class DealSystem
{
    private const long ActionCooldownMs = 120;
    private const long PendingTimeoutMs = 10_000;

    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    private MirDealMessageHandlers.DealPendingAction _pendingAction;
    private ClientItem _pendingItem;
    private long _pendingSinceMs;
    private long _lastActionMs;

    public DealSystem(MirClientSession session, MirWorldState world, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public bool HasPending => _pendingAction != MirDealMessageHandlers.DealPendingAction.None;

    public MirDealMessageHandlers.DealItemPending? GetPending()
    {
        MirDealMessageHandlers.DealPendingAction action = _pendingAction;
        if (action == MirDealMessageHandlers.DealPendingAction.None)
            return null;

        return new MirDealMessageHandlers.DealItemPending(action, _pendingItem);
    }

    public void ClearPending()
    {
        _pendingAction = MirDealMessageHandlers.DealPendingAction.None;
        _pendingItem = default;
        _pendingSinceMs = 0;
    }

    public bool TryClearExpiredPending(long nowMs)
    {
        if (_pendingAction == MirDealMessageHandlers.DealPendingAction.None)
            return false;

        if (nowMs - _pendingSinceMs < PendingTimeoutMs)
            return false;

        _log?.Invoke($"[deal] pending cleared (timeout action={_pendingAction} makeIndex={_pendingItem.MakeIndex})");
        ClearPending();
        return true;
    }

    public async ValueTask TrySendDelItemAsync(ClientItem item, int dealIndex, CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        long nowMs = Environment.TickCount64;

        if (HasPending)
        {
            if (!TryClearExpiredPending(nowMs))
            {
                _log?.Invoke($"[deal] action ignored (pending {_pendingAction} makeIndex={_pendingItem.MakeIndex})");
                return;
            }
        }

        if (nowMs - _lastActionMs < ActionCooldownMs)
            return;

        _lastActionMs = nowMs;
        _pendingAction = MirDealMessageHandlers.DealPendingAction.DelItem;
        _pendingItem = item;
        _pendingSinceMs = nowMs;

        try
        {
            await _session.SendClientStringAsync(Grobal2.CM_DEALDELITEM, item.MakeIndex, 0, 0, item.Dura, item.NameString, token);
            _log?.Invoke($"[deal] CM_DEALDELITEM '{item.NameString}' makeIndex={item.MakeIndex} idx={dealIndex}");
        }
        catch (Exception ex)
        {
            ClearPending();
            _log?.Invoke($"[deal] CM_DEALDELITEM send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async ValueTask TrySendAddItemAsync(ClientItem item, int bagSlotIndex, CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        long nowMs = Environment.TickCount64;

        if (HasPending)
        {
            if (!TryClearExpiredPending(nowMs))
            {
                _log?.Invoke($"[deal] action ignored (pending {_pendingAction} makeIndex={_pendingItem.MakeIndex})");
                return;
            }
        }

        if (nowMs - _lastActionMs < ActionCooldownMs)
            return;

        _lastActionMs = nowMs;
        _pendingAction = MirDealMessageHandlers.DealPendingAction.AddItem;
        _pendingItem = item;
        _pendingSinceMs = nowMs;

        try
        {
            await _session.SendClientStringAsync(Grobal2.CM_DEALADDITEM, item.MakeIndex, 0, 0, item.Dura, item.NameString, token);
            _log?.Invoke($"[deal] CM_DEALADDITEM '{item.NameString}' makeIndex={item.MakeIndex} slot={bagSlotIndex}");
        }
        catch (Exception ex)
        {
            ClearPending();
            _log?.Invoke($"[deal] CM_DEALADDITEM send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async ValueTask TrySendCancelAsync(CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        long nowMs = Environment.TickCount64;
        if (nowMs - _lastActionMs < ActionCooldownMs)
            return;

        _lastActionMs = nowMs;
        ClearPending();

        try
        {
            await _session.SendClientMessageAsync(Grobal2.CM_DEALCANCEL, 0, 0, 0, 0, token);
            _log?.Invoke("[deal] CM_DEALCANCEL");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[deal] CM_DEALCANCEL send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async ValueTask TrySendEndAsync(CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        long nowMs = Environment.TickCount64;
        if (nowMs - _lastActionMs < ActionCooldownMs)
            return;

        _lastActionMs = nowMs;

        try
        {
            await _session.SendClientMessageAsync(Grobal2.CM_DEALEND, 0, 0, 0, 0, token);
            _log?.Invoke("[deal] CM_DEALEND");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[deal] CM_DEALEND send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async ValueTask TrySendChangeGoldAsync(int gold, CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet)
            return;

        long nowMs = Environment.TickCount64;
        if (nowMs - _lastActionMs < ActionCooldownMs)
            return;

        _lastActionMs = nowMs;

        try
        {
            await _session.SendClientMessageAsync(Grobal2.CM_DEALCHGGOLD, gold, 0, 0, 0, token);
            _log?.Invoke($"[deal] CM_DEALCHGGOLD gold={gold}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[deal] CM_DEALCHGGOLD send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
