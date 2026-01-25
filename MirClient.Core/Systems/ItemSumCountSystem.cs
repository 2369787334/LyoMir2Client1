using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public sealed class ItemSumCountSystem
{
    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly InventoryPendingSystem _pending;
    private readonly Action<string>? _log;

    public ItemSumCountSystem(MirClientSession session, MirWorldState world, InventoryPendingSystem pending, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _log = log;
    }

    public async ValueTask TryItemSumCountAsync(
        bool heroBag,
        int orgMakeIndex,
        int exMakeIndex,
        string orgName,
        string exName,
        string logPrefix,
        string successSuffix,
        CancellationToken token)
    {
        if (orgMakeIndex == 0 || exMakeIndex == 0)
            return;

        long nowMs = Environment.TickCount64;

        if (_pending.ItemSumCountMakeIndex != 0)
        {
            if (_pending.IsItemSumCountPendingActive(nowMs))
            {
                _log?.Invoke($"{logPrefix} CM_ITEMSUMCOUNT ignored (waiting makeIndex={_pending.ItemSumCountMakeIndex})");
                return;
            }

            _log?.Invoke($"[item] pending item sum count cleared (timeout makeIndex={_pending.ItemSumCountMakeIndex})");
            _pending.RestoreItemSumCountToBag();
        }

        ClientItem removed;
        bool removedOk = heroBag
            ? _world.TryRemoveHeroBagItemByMakeIndex(exMakeIndex, out removed)
            : _world.TryRemoveBagItemByMakeIndex(exMakeIndex, out removed);

        if (!removedOk)
        {
            _log?.Invoke($"{logPrefix} CM_ITEMSUMCOUNT ignored (source not found makeIndex={exMakeIndex})");
            return;
        }

        ushort lo = unchecked((ushort)(exMakeIndex & 0xFFFF));
        ushort hi = unchecked((ushort)((exMakeIndex >> 16) & 0xFFFF));
        ushort heroFlag = heroBag ? (ushort)1 : (ushort)0;
        string body = $"{orgName}/{exName}";

        _pending.SetItemSumCountPending(heroBag, orgMakeIndex, exMakeIndex, removed, nowMs);

        try
        {
            await _session.SendClientStringAsync(
                Grobal2.CM_ITEMSUMCOUNT,
                orgMakeIndex,
                lo,
                hi,
                heroFlag,
                body,
                token);
            _log?.Invoke($"{logPrefix} CM_ITEMSUMCOUNT '{body}' orgMakeIndex={orgMakeIndex} exMakeIndex={exMakeIndex}{successSuffix}");
        }
        catch (Exception ex)
        {
            _pending.RestoreItemSumCountToBag();
            _log?.Invoke($"{logPrefix} CM_ITEMSUMCOUNT send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

