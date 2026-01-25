using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public sealed class EquipSystem
{
    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly InventoryPendingSystem _pending;
    private readonly Action<string>? _log;

    private bool _takeOnPosToggle = true;

    public EquipSystem(MirClientSession session, MirWorldState world, InventoryPendingSystem pending, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _log = log;
    }

    public async ValueTask TryTakeOffAsync(
        ClientItem item,
        bool heroEquip,
        int where,
        string logPrefix,
        string actionLabel,
        string successSuffix,
        CancellationToken token)
    {
        if (item.MakeIndex == 0 || string.IsNullOrWhiteSpace(item.NameString))
            return;

        long nowMs = Environment.TickCount64;
        if (_pending.IsUseItemPendingActive(nowMs))
        {
            _log?.Invoke($"{logPrefix} {actionLabel} ignored (waiting makeIndex={_pending.UseItemMakeIndex} where={_pending.UseItemWhere})");
            return;
        }

        _pending.ClearUseItemPending();

        if (where < Grobal2.U_DRESS || where > Grobal2.U_CHARM)
        {
            _log?.Invoke($"{logPrefix} {actionLabel} ignored (invalid where={where})");
            return;
        }

        ushort cmd = heroEquip ? Grobal2.CM_HEROTAKEOFFITEM : Grobal2.CM_TAKEOFFITEM;
        string cmdName = heroEquip ? "CM_HEROTAKEOFFITEM" : "CM_TAKEOFFITEM";

        if (!_world.TryRemoveUseItemBySlot(where, heroEquip, out ClientItem removed))
        {
            _log?.Invoke($"{logPrefix} {actionLabel} failed: makeIndex={item.MakeIndex} where={where} (not found)");
            return;
        }

        _pending.SetUseItemPending(heroEquip, takeOff: true, removed, where, nowMs);

        try
        {
            await _session.SendClientStringAsync(cmd, removed.MakeIndex, (ushort)where, 0, 0, removed.NameString, token);
            _log?.Invoke($"{logPrefix} {cmdName} '{removed.NameString}' makeIndex={removed.MakeIndex} where={where}{successSuffix}");
        }
        catch (Exception ex)
        {
            _pending.ClearUseItemPending();
            _world.SetUseItemSlot(where, heroEquip, removed);
            _log?.Invoke($"{logPrefix} {cmdName} send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async ValueTask TryTakeOnAsync(
        ClientItem item,
        bool heroEquip,
        int whereHint,
        int bagSlotIndex,
        string logPrefix,
        string actionLabel,
        string successSuffix,
        CancellationToken token)
    {
        if (item.MakeIndex == 0 || string.IsNullOrWhiteSpace(item.NameString))
            return;

        long nowMs = Environment.TickCount64;
        if (_pending.IsUseItemPendingActive(nowMs))
        {
            _log?.Invoke($"{logPrefix} {actionLabel} ignored (waiting makeIndex={_pending.UseItemMakeIndex} where={_pending.UseItemWhere})");
            return;
        }

        _pending.ClearUseItemPending();

        IReadOnlyDictionary<int, ClientItem> useItemsForResolve = heroEquip ? _world.HeroUseItems : _world.UseItems;

        int where = whereHint;
        if (where < 0 || !EquipSlotResolver.IsValidTakeOnSlotForStdMode(item.S.StdMode, where))
        {
            where = EquipSlotResolver.TryResolveTakeOnSlot(item, useItemsForResolve, ref _takeOnPosToggle, out int resolved)
                ? resolved
                : -1;
        }

        if (where < 0)
        {
            string extra = bagSlotIndex >= 0 ? $" slot={bagSlotIndex}" : string.Empty;
            _log?.Invoke($"{logPrefix} {actionLabel} not implemented: stdMode={item.S.StdMode} name='{item.NameString}' makeIndex={item.MakeIndex}{extra}");
            return;
        }

        ushort cmd = heroEquip ? Grobal2.CM_HEROTAKEONITEM : Grobal2.CM_TAKEONITEM;
        string cmdName = heroEquip ? "CM_HEROTAKEONITEM" : "CM_TAKEONITEM";

        ClientItem removed;
        bool removedOk = heroEquip
            ? _world.TryRemoveHeroBagItemByMakeIndex(item.MakeIndex, out removed)
            : _world.TryRemoveBagItemByMakeIndex(item.MakeIndex, out removed);

        if (!removedOk)
        {
            string extra = bagSlotIndex >= 0 ? $" slot={bagSlotIndex}" : string.Empty;
            _log?.Invoke($"{logPrefix} {actionLabel} failed: makeIndex={item.MakeIndex} where={where}{extra} (not found)");
            return;
        }

        _pending.SetUseItemPending(heroEquip, takeOff: false, removed, where, nowMs);

        try
        {
            await _session.SendClientStringAsync(cmd, removed.MakeIndex, (ushort)where, 0, 0, removed.NameString, token);
            string slotLabel = bagSlotIndex >= 0 ? $" slot={bagSlotIndex}" : string.Empty;
            _log?.Invoke($"{logPrefix} {cmdName} '{removed.NameString}' makeIndex={removed.MakeIndex} where={where}{slotLabel}{successSuffix}");
        }
        catch (Exception ex)
        {
            _pending.ClearUseItemPending();

            if (heroEquip)
                _world.RestoreHeroBagItem(removed);
            else
                _world.RestoreBagItem(removed);

            _log?.Invoke($"{logPrefix} {cmdName} send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
