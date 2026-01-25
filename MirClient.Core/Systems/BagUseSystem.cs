using MirClient.Protocol;
using MirClient.Protocol.Packets;
using MirClient.Core.World;

namespace MirClient.Core.Systems;

public sealed class BagUseSystem
{
    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly InventoryPendingSystem _pending;
    private readonly Action<string>? _log;

    public BagUseSystem(MirClientSession session, MirWorldState world, InventoryPendingSystem pending, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _log = log;
    }

    public async ValueTask TryEatAsync(ClientItem item, bool heroBag, string logPrefix, int slotIndex, CancellationToken token)
    {
        if (item.MakeIndex == 0 || string.IsNullOrWhiteSpace(item.NameString))
            return;

        long nowMs = Environment.TickCount64;
        if (_pending.IsEatPendingActive(nowMs))
        {
            _log?.Invoke($"{logPrefix} eat ignored (waiting makeIndex={_pending.EatMakeIndex})");
            return;
        }

        _pending.ClearEatPending();

        ClientItem removed;
        bool removedOk = heroBag
            ? _world.TryRemoveHeroBagItemByMakeIndex(item.MakeIndex, out removed)
            : _world.TryRemoveBagItemByMakeIndex(item.MakeIndex, out removed);

        if (!removedOk)
        {
            _log?.Invoke($"{logPrefix} eat failed: makeIndex={item.MakeIndex} slot={slotIndex} (not found)");
            return;
        }

        _pending.SetEatPending(heroBag, removed, slotIndex, nowMs);

        ushort cmd = heroBag ? Grobal2.CM_HEROEAT : Grobal2.CM_EAT;
        string cmdName = heroBag ? "CM_HEROEAT" : "CM_EAT";
        ushort type = heroBag && removed.S.StdMode == 42 ? (ushort)1 : (ushort)0;

        try
        {
            await _session.SendClientMessageAsync(cmd, removed.MakeIndex, type, 0, removed.S.StdMode, token);
            _log?.Invoke($"{logPrefix} {cmdName} '{removed.NameString}' makeIndex={removed.MakeIndex} slot={slotIndex}");
        }
        catch (Exception ex)
        {
            _pending.RestoreEatPendingToBag();
            _log?.Invoke($"{logPrefix} {cmdName} send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async ValueTask TryDismantleAsync(ClientItem item, bool heroBag, string logPrefix, int slotIndex, CancellationToken token)
    {
        if (item.MakeIndex == 0 || string.IsNullOrWhiteSpace(item.NameString))
            return;

        ushort heroFlag = heroBag ? (ushort)1 : (ushort)0;

        try
        {
            await _session.SendClientStringAsync(Grobal2.CM_DISMANTLEITEM, item.MakeIndex, 1, heroFlag, 0, item.NameString, token);
            _log?.Invoke($"{logPrefix} CM_DISMANTLEITEM '{item.NameString}' makeIndex={item.MakeIndex} count=1 slot={slotIndex}");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"{logPrefix} CM_DISMANTLEITEM send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
