using MirClient.Core.World;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public sealed class MarketSystem
{
    private const long ActionCooldownMs = 120;

    private readonly MaketSystem _maketSystem;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    private long _lastActionMs;

    public bool Visible { get; private set; }
    public int TopIndex { get; private set; }
    public int SelectedIndex { get; private set; } = -1;
    public string FindText { get; private set; } = string.Empty;

    public MarketSystem(MaketSystem maketSystem, MirWorldState world, Action<string>? log = null)
    {
        _maketSystem = maketSystem ?? throw new ArgumentNullException(nameof(maketSystem));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public void HandleServerList(bool first)
    {
        Visible = true;

        if (first)
        {
            TopIndex = 0;
            SelectedIndex = -1;
        }
    }

    public void Reset(bool clearWorld)
    {
        Visible = false;
        TopIndex = 0;
        SelectedIndex = -1;
        FindText = string.Empty;

        if (clearWorld)
            _world.ClearMarket();
    }

    public bool TryBeginAction()
    {
        long nowMs = Environment.TickCount64;
        if (nowMs - _lastActionMs < ActionCooldownMs)
            return false;

        _lastActionMs = nowMs;
        return true;
    }

    public async ValueTask TryCloseAsync(CancellationToken token)
    {
        if (!TryBeginAction())
            return;

        await _maketSystem.TrySendMarketCloseAsync(token);
        Reset(clearWorld: true);
    }

    public async ValueTask TryRefreshAsync(int merchantId, CancellationToken token)
    {
        if (!TryBeginAction())
            return;

        if (merchantId <= 0)
        {
            _log?.Invoke("[market] refresh ignored: merchant not set");
            return;
        }

        TopIndex = 0;
        SelectedIndex = -1;
        FindText = string.Empty;

        await _maketSystem.TrySendMarketListAsync(merchantId, 0, string.Empty, token);
    }

    public async ValueTask TryFindAsync(int merchantId, string? findInput, CancellationToken token)
    {
        if (!TryBeginAction())
            return;

        if (_world.MarketUserMode != 1)
            return;

        if (merchantId <= 0)
        {
            _log?.Invoke("[market] find ignored: merchant not set");
            return;
        }

        string find = (findInput ?? string.Empty).Trim();
        if (find.Length > 14)
            find = find[..14];
        if (find.Length == 0)
            return;

        TopIndex = 0;
        SelectedIndex = -1;
        FindText = find;

        await _maketSystem.TrySendMarketListAsync(merchantId, 2, find, token);
    }

    public void TryPrev()
    {
        if (!TryBeginAction())
            return;

        TopIndex = Math.Max(0, TopIndex - 10);
    }

    public async ValueTask TryNextAsync(int merchantId, CancellationToken token)
    {
        if (!TryBeginAction())
            return;

        int nextTop = Math.Max(0, TopIndex + 10);
        if (nextTop < _world.MarketItems.Count)
        {
            TopIndex = nextTop;
            return;
        }

        if (_world.MarketCurrentPage >= _world.MarketMaxPage)
            return;

        if (merchantId <= 0)
        {
            _log?.Invoke("[market] next ignored: merchant not set");
            return;
        }

        await _maketSystem.TrySendMarketListAsync(merchantId, 1, string.Empty, token);
    }

    public async ValueTask TryActionAsync(int merchantId, CancellationToken token)
    {
        if (!TryBeginAction())
            return;

        if (merchantId <= 0)
        {
            _log?.Invoke("[market] action ignored: merchant not set");
            return;
        }

        if ((uint)SelectedIndex >= (uint)_world.MarketItems.Count)
            return;

        MarketItem selected = _world.MarketItems[SelectedIndex];
        await _maketSystem.TrySendMarketActionAsync(merchantId, selected, token);
    }

    public void SelectRow(int index, MarketItem item, bool logUi)
    {
        SelectedIndex = index;
        if (index < TopIndex)
            TopIndex = index;

        if (logUi)
            _log?.Invoke($"[market] select idx={index} name='{item.Item.NameString}' price={item.SellPrice} state={item.SellState}");
    }
}

