using System.Runtime.InteropServices;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public sealed class HeroBagExchangeSystem
{
    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly InventoryPendingSystem _pending;
    private readonly Action<string>? _log;

    public HeroBagExchangeSystem(MirClientSession session, MirWorldState world, InventoryPendingSystem pending, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _pending = pending ?? throw new ArgumentNullException(nameof(pending));
        _log = log;
    }

    public bool TryBeginExchange(long nowMs)
    {
        if (_pending.IsHeroBagExchangePendingActive(nowMs))
        {
            _log?.Invoke($"[hero-bag] exchange ignored (waiting dir={_pending.HeroBagExchangeDirectionLabel} makeIndex={_pending.HeroBagExchangeMakeIndex})");
            return false;
        }

        _pending.ClearHeroBagExchangePending();
        return true;
    }

    public async ValueTask TrySendExchangeAsync(bool heroToPlayer, int makeIndex, int slotIndex, long nowMs, CancellationToken token)
    {
        if (makeIndex == 0)
            return;

        string prefix = heroToPlayer ? "[hero-bag]" : "[bag]";

        ClientItem removed;
        bool removedOk = heroToPlayer
            ? _world.TryRemoveHeroBagItemByMakeIndex(makeIndex, out removed)
            : _world.TryRemoveBagItemByMakeIndex(makeIndex, out removed);

        if (!removedOk)
        {
            _log?.Invoke($"{prefix} Alt+Shift+Click exchange failed: makeIndex={makeIndex} slot={slotIndex} (not found)");
            return;
        }

        _pending.SetHeroBagExchangePending(heroToPlayer, removed, nowMs);

        ushort cmd = heroToPlayer ? Grobal2.CM_HEROADDITEMTOPLAYER : Grobal2.CM_PLAYERADDITEMTOHERO;
        string cmdName = heroToPlayer ? "CM_HEROADDITEMTOPLAYER" : "CM_PLAYERADDITEMTOHERO";

        try
        {
            await _session.SendClientStringAsync(cmd, removed.MakeIndex, 0, 0, 0, removed.NameString, token);
            _log?.Invoke($"{prefix} {cmdName} '{removed.NameString}' makeIndex={removed.MakeIndex} slot={slotIndex}");
        }
        catch (Exception ex)
        {
            _pending.ClearHeroBagExchangePending();

            string encoded = EncodeClientItem(removed);
            if (heroToPlayer)
            {
                _ = _world.TryApplyHeroAddBagItem(encoded, out _);
            }
            else
            {
                _ = _world.TryApplyAddBagItem(encoded, out _);
            }

            _log?.Invoke($"{prefix} {cmdName} send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    public async ValueTask TryExchangeAsync(bool heroToPlayer, int makeIndex, int slotIndex, CancellationToken token)
    {
        long nowMs = Environment.TickCount64;
        if (!TryBeginExchange(nowMs))
            return;

        await TrySendExchangeAsync(heroToPlayer, makeIndex, slotIndex, nowMs, token);
    }

    private static string EncodeClientItem(ClientItem item)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref item, 1));
        return EdCode.EncodeBuffer(bytes);
    }
}
