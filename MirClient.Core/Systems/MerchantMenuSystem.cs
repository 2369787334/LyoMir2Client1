using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class MerchantMenuSystem
{
    private const long ActionCooldownMs = 120;
    private const int DetailPageStep = 10;

    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    private long _lastActionMs;
    private int _merchantId;
    private MirMerchantMode _mode;

    public int TopIndex { get; private set; }
    public int SelectedIndex { get; private set; } = -1;
    public string DetailItemName { get; private set; } = string.Empty;

    public MerchantMenuSystem(MirClientSession session, MirWorldState world, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public void Reset()
    {
        _lastActionMs = 0;
        _merchantId = 0;
        _mode = MirMerchantMode.None;
        TopIndex = 0;
        SelectedIndex = -1;
        DetailItemName = string.Empty;
    }

    public void Sync(int merchantId, MirMerchantMode mode, int goodsCount, int maxLines)
    {
        if (merchantId != _merchantId)
        {
            _merchantId = merchantId;
            _mode = mode;
            TopIndex = 0;
            SelectedIndex = -1;
            DetailItemName = string.Empty;
        }

        if (_mode != mode)
        {
            _mode = mode;
            TopIndex = 0;
            SelectedIndex = -1;
        }

        if (SelectedIndex >= goodsCount)
            SelectedIndex = -1;

        if (mode == MirMerchantMode.DetailMenu)
        {
            TopIndex = 0;
            return;
        }

        if (maxLines <= 0)
            return;

        int maxTop = Math.Max(0, goodsCount - maxLines);
        TopIndex = Math.Clamp(TopIndex, 0, maxTop);
    }

    public void SelectItem(int index, IReadOnlyList<MirMerchantGoods> goods, bool logUi)
    {
        if ((uint)index >= (uint)goods.Count)
            return;

        SelectedIndex = index;

        if (!logUi)
            return;

        MirMerchantGoods selected = goods[index];
        if (!string.IsNullOrWhiteSpace(selected.Name))
            _log?.Invoke($"[shop] select '{selected.Name}' index={index}");
    }

    public void TryScrollPrev(int goodsCount, int maxLines)
    {
        if (goodsCount <= 0 || maxLines <= 0)
            return;

        if (!TryBeginAction())
            return;

        int pageStep = Math.Max(1, maxLines - 1);
        TopIndex = Math.Max(0, TopIndex - pageStep);
    }

    public void TryScrollNext(int goodsCount, int maxLines)
    {
        if (goodsCount <= 0 || maxLines <= 0)
            return;

        if (!TryBeginAction())
            return;

        int maxTop = Math.Max(0, goodsCount - maxLines);
        int pageStep = Math.Max(1, maxLines - 1);
        if (TopIndex + maxLines < goodsCount)
            TopIndex = Math.Min(maxTop, TopIndex + pageStep);
    }

    public async ValueTask TryDetailPrevAsync(int merchantId, int currentTopLine, CancellationToken token)
    {
        if (!TryBeginAction())
            return;

        if (merchantId <= 0)
        {
            _log?.Invoke("[shop] detail page ignored: merchant not set");
            return;
        }

        if (string.IsNullOrWhiteSpace(DetailItemName))
        {
            _log?.Invoke("[shop] detail page ignored: no detail item name");
            return;
        }

        int topLine = Math.Max(0, currentTopLine);
        int nextTopLine = Math.Max(0, topLine - DetailPageStep);
        await TrySendDetailAsync(merchantId, nextTopLine, DetailItemName, token);
    }

    public async ValueTask TryDetailNextAsync(int merchantId, int currentTopLine, CancellationToken token)
    {
        if (!TryBeginAction())
            return;

        if (merchantId <= 0)
        {
            _log?.Invoke("[shop] detail page ignored: merchant not set");
            return;
        }

        if (string.IsNullOrWhiteSpace(DetailItemName))
        {
            _log?.Invoke("[shop] detail page ignored: no detail item name");
            return;
        }

        int topLine = Math.Max(0, currentTopLine);
        int nextTopLine = topLine + DetailPageStep;
        await TrySendDetailAsync(merchantId, nextTopLine, DetailItemName, token);
    }

    public async ValueTask TryActionAsync(int merchantId, MirMerchantMode mode, MirMerchantGoods selected, ushort buyCount, CancellationToken token)
    {
        if (!TryBeginAction())
            return;

        if (merchantId <= 0)
        {
            _log?.Invoke("[shop] action ignored: merchant not set");
            return;
        }

        string itemName = (selected.Name ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(itemName))
        {
            _log?.Invoke("[shop] action ignored: empty item name");
            return;
        }

        if (selected.SubMenu > 0 && selected.SubMenu != 2)
        {
            DetailItemName = itemName;
            await TrySendDetailAsync(merchantId, topLine: 0, itemName, token);
            return;
        }

        if (mode == MirMerchantMode.MakeDrug)
        {
            try
            {
                await _session.SendClientStringAsync(Grobal2.CM_USERMAKEDRUGITEM, merchantId, 0, 0, 0, itemName, token);
                _log?.Invoke($"[shop] CM_USERMAKEDRUGITEM merchant={merchantId} '{itemName}'");
            }
            catch (Exception ex)
            {
                _log?.Invoke($"[shop] CM_USERMAKEDRUGITEM send failed: {ex.GetType().Name}: {ex.Message}");
            }

            return;
        }

        if (selected.SubMenu == 2)
        {
            if (buyCount < 1)
            {
                _log?.Invoke("[shop] buy ignored: count not set");
                return;
            }

            buyCount = Math.Clamp(buyCount, (ushort)1, (ushort)9999);
        }

        int itemServerIndex = selected.Stock;
        ushort lo = unchecked((ushort)(itemServerIndex & 0xFFFF));
        ushort hi = unchecked((ushort)((itemServerIndex >> 16) & 0xFFFF));

        try
        {
            await _session.SendClientStringAsync(Grobal2.CM_USERBUYITEM, merchantId, lo, hi, buyCount, itemName, token);
            _log?.Invoke($"[shop] CM_USERBUYITEM merchant={merchantId} idx={itemServerIndex} count={buyCount} '{itemName}'");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[shop] CM_USERBUYITEM send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private bool TryBeginAction()
    {
        long nowMs = Environment.TickCount64;
        if (nowMs - _lastActionMs < ActionCooldownMs)
            return false;

        _lastActionMs = nowMs;
        return true;
    }

    private async ValueTask TrySendDetailAsync(int merchantId, int topLine, string itemName, CancellationToken token)
    {
        ushort topLineU16 = unchecked((ushort)Math.Clamp(topLine, 0, 65535));

        try
        {
            await _session.SendClientStringAsync(Grobal2.CM_USERGETDETAILITEM, merchantId, topLineU16, 0, 0, itemName, token);
            _log?.Invoke($"[shop] CM_USERGETDETAILITEM merchant={merchantId} topLine={topLine} '{itemName}'");
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[shop] CM_USERGETDETAILITEM send failed: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
