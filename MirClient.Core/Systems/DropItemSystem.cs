using MirClient.Protocol;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public sealed class DropItemSystem
{
    private readonly MirClientSession _session;
    private readonly InventoryPendingSystem _pending;
    private readonly Action<string>? _log;

    public DropItemSystem(MirClientSession session, InventoryPendingSystem pending, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _log = log;
    }

    public async ValueTask TryDropAsync(
        ClientItem item,
        bool heroBag,
        string logPrefix,
        int slotIndex,
        string actionLabel,
        string successSuffix,
        CancellationToken token)
    {
        if (item.MakeIndex == 0 || string.IsNullOrWhiteSpace(item.NameString))
            return;

        long nowMs = Environment.TickCount64;
        if (_pending.IsDropPendingActive(nowMs))
        {
            _log?.Invoke($"{logPrefix} {actionLabel} ignored (waiting makeIndex={_pending.DropMakeIndex})");
            return;
        }

        _pending.ClearDropPending();

        int dropCount = item.S.Overlap > 0 ? item.Dura : 0;
        _pending.SetDropPending(item.MakeIndex, nowMs);

        ushort cmd = heroBag ? Grobal2.CM_HERODROPITEM : Grobal2.CM_DROPITEM;
        string cmdName = heroBag ? "CM_HERODROPITEM" : "CM_DROPITEM";
        string slotLabel = slotIndex >= 0 ? $" slot={slotIndex}" : string.Empty;

        try
        {
            await _session.SendClientStringAsync(cmd, item.MakeIndex, unchecked((ushort)dropCount), 0, 0, item.NameString, token);
            _log?.Invoke($"{logPrefix} {cmdName} '{item.NameString}' makeIndex={item.MakeIndex} count={dropCount}{slotLabel}{successSuffix}");
        }
        catch (Exception ex)
        {
            _pending.ClearDropPending();
            _log?.Invoke($"{logPrefix} {cmdName} send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}

