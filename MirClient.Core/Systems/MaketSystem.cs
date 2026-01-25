using System.Runtime.InteropServices;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public sealed class MaketSystem
{
    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    public MaketSystem(MirClientSession session, MirWorldState world, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public async Task<bool> TrySendMarketCloseAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _session.SendClientMessageAsync(Grobal2.CM_MARKET_CLOSE, 0, 0, 0, 0, cancellationToken).ConfigureAwait(false);
            _log?.Invoke("[market] CM_MARKET_CLOSE");
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[market] CM_MARKET_CLOSE send failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TrySendMarketListAsync(int merchantId, ushort type, string? find, CancellationToken cancellationToken)
    {
        find ??= string.Empty;

        try
        {
            await _session.SendClientStringAsync(Grobal2.CM_MARKET_LIST, merchantId, type, 0, 0, find, cancellationToken).ConfigureAwait(false);
            _log?.Invoke(type == 2
                ? $"[market] CM_MARKET_LIST merchant={merchantId} type=2 find='{find}'"
                : $"[market] CM_MARKET_LIST merchant={merchantId} type={type}");
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[market] CM_MARKET_LIST send failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TrySendMarketActionAsync(int merchantId, MarketItem selected, CancellationToken cancellationToken)
    {
        ushort cmd;
        string cmdName;

        if (_world.MarketUserMode == 1)
        {
            cmd = Grobal2.CM_MARKET_BUY;
            cmdName = "CM_MARKET_BUY";
        }
        else if (_world.MarketUserMode == 2)
        {
            if (selected.SellState == 2)
            {
                cmd = Grobal2.CM_MARKET_GETPAY;
                cmdName = "CM_MARKET_GETPAY";
            }
            else
            {
                cmd = Grobal2.CM_MARKET_CANCEL;
                cmdName = "CM_MARKET_CANCEL";
            }
        }
        else
        {
            return false;
        }

        int sellIndex = selected.Index;
        ushort lo = unchecked((ushort)(sellIndex & 0xFFFF));
        ushort hi = unchecked((ushort)((sellIndex >> 16) & 0xFFFF));

        try
        {
            await _session.SendClientMessageAsync(cmd, merchantId, lo, hi, 0, cancellationToken).ConfigureAwait(false);
            _log?.Invoke($"[market] {cmdName} merchant={merchantId} sellIndex={sellIndex} name='{selected.Item.NameString}' state={selected.SellState}");
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[market] {cmdName} send failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TrySendMarketSellAsync(
        int merchantId,
        ClientItem item,
        ushort duraOrCount,
        int price,
        int bagSlotIndex,
        CancellationToken cancellationToken)
    {
        if (merchantId <= 0)
            return false;

        ushort lo = unchecked((ushort)(item.MakeIndex & 0xFFFF));
        ushort hi = unchecked((ushort)((item.MakeIndex >> 16) & 0xFFFF));

        try
        {
            await _session.SendClientStringAsync(
                    Grobal2.CM_MARKET_SELL,
                    merchantId,
                    lo,
                    hi,
                    duraOrCount,
                    price.ToString(),
                    cancellationToken)
                .ConfigureAwait(false);
            _log?.Invoke($"[market] CM_MARKET_SELL makeIndex={item.MakeIndex} price={price} dura={duraOrCount} slot={bagSlotIndex}");
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[market] CM_MARKET_SELL send failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TrySendUserBuyItemAsync(int merchantActorId, ClientItem selected, CancellationToken cancellationToken)
    {
        ushort lo = unchecked((ushort)(selected.MakeIndex & 0xFFFF));
        ushort hi = unchecked((ushort)((selected.MakeIndex >> 16) & 0xFFFF));
        ushort count = selected.S.Overlap > 0 ? selected.Dura : (ushort)1;

        try
        {
            await _session.SendClientStringAsync(Grobal2.CM_USERBUYITEM, merchantActorId, lo, hi, count, selected.NameString, cancellationToken).ConfigureAwait(false);
            _log?.Invoke($"[stall] CM_USERBUYITEM '{selected.NameString}' makeIndex={selected.MakeIndex} count={count} actor={merchantActorId}");
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[stall] CM_USERBUYITEM send failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TrySendOpenStallAsync(int count, ClientStallItems request, CancellationToken cancellationToken)
    {
        if (count <= 0)
            return false;

        if (!_world.MyselfRecogIdSet || _world.MyselfRecogId == 0)
            return false;

        byte[] body = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref request, 1)).ToArray();

        try
        {
            await _session.SendClientBufferAsync(Grobal2.CM_OPENSTALL, _world.MyselfRecogId, 0, 0, unchecked((ushort)count), body, cancellationToken).ConfigureAwait(false);
            _log?.Invoke($"[stall] CM_OPENSTALL cnt={count} name='{request.NameString}'");
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[stall] CM_OPENSTALL send failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TrySendCloseStallAsync(CancellationToken cancellationToken)
    {
        if (!_world.MyselfRecogIdSet || _world.MyselfRecogId == 0)
            return false;

        var empty = default(ClientStallItems);
        byte[] body = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref empty, 1)).ToArray();

        try
        {
            await _session.SendClientBufferAsync(Grobal2.CM_OPENSTALL, _world.MyselfRecogId, 0, 0, 0, body, cancellationToken).ConfigureAwait(false);
            _log?.Invoke("[stall] CM_OPENSTALL cnt=0 (close)");
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[stall] CM_OPENSTALL(close) send failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TrySendUpdateStallItemRemoveAsync(int makeIndex, CancellationToken cancellationToken)
    {
        var removeItem = new ClientStall { MakeIndex = makeIndex };
        byte[] body = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref removeItem, 1)).ToArray();

        try
        {
            await _session.SendClientBufferAsync(Grobal2.CM_UPDATESTALLITEM, 0, 0, 0, 0, body, cancellationToken).ConfigureAwait(false);
            _log?.Invoke($"[stall] CM_UPDATESTALLITEM remove makeIndex={makeIndex}");
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[stall] CM_UPDATESTALLITEM(remove) send failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TrySendUpdateStallItemAddAsync(int makeIndex, int price, byte goldType, CancellationToken cancellationToken)
    {
        var stall = new ClientStall { MakeIndex = makeIndex, Price = price, GoldType = goldType };
        byte[] body = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref stall, 1)).ToArray();

        try
        {
            await _session.SendClientBufferAsync(Grobal2.CM_UPDATESTALLITEM, 0, 0, 0, 1, body, cancellationToken).ConfigureAwait(false);
            _log?.Invoke($"[stall] CM_UPDATESTALLITEM add makeIndex={makeIndex} price={price} type={goldType}");
            return true;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[stall] CM_UPDATESTALLITEM(add) send failed: {ex.GetType().Name}: {ex.Message}");
            return false;
        }
    }
}
