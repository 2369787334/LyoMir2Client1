using MirClient.Core.Messages;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public sealed class YbDealSystem
{
    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    private long _lastActionMs;

    public YbDealSystem(MirClientSession session, MirWorldState world, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public bool Visible { get; private set; }
    public MirYbDealDialogMode Mode { get; private set; } = MirYbDealDialogMode.Sell;
    public int PostPrice { get; private set; }
    public string CharName { get; private set; } = string.Empty;
    public string TargetName { get; private set; } = string.Empty;
    public string PostTime { get; private set; } = string.Empty;
    public ClientItem[] Items { get; private set; } = Array.Empty<ClientItem>();
    public int SelectedIndex { get; private set; } = -1;

    public void ShowDialog(MirYbDealDialog dialog, bool logUi)
    {
        Visible = true;
        Mode = dialog.Mode;
        PostPrice = dialog.PostPrice;
        CharName = dialog.CharName ?? string.Empty;
        TargetName = dialog.TargetName ?? string.Empty;
        PostTime = dialog.PostTime ?? string.Empty;
        Items = dialog.Items ?? Array.Empty<ClientItem>();
        SelectedIndex = Items.Length > 0 ? 0 : -1;

        if (logUi)
            _log?.Invoke($"[ui] yb deal opened mode={Mode} price={PostPrice} items={Items.Length}");
    }

    public void Close(bool logUi)
    {
        if (!Visible)
            return;

        Visible = false;
        Mode = MirYbDealDialogMode.Sell;
        PostPrice = 0;
        CharName = string.Empty;
        TargetName = string.Empty;
        PostTime = string.Empty;
        Items = Array.Empty<ClientItem>();
        SelectedIndex = -1;

        if (logUi)
            _log?.Invoke("[ui] yb deal closed");
    }

    public void SetSelectedIndex(int index)
    {
        if (!Visible)
            return;

        if (Items.Length <= 0)
        {
            SelectedIndex = -1;
            return;
        }

        if (index < 0)
        {
            SelectedIndex = 0;
            return;
        }

        SelectedIndex = Math.Clamp(index, 0, Items.Length - 1);
    }

    public void ClampSelectionToShownItems(int shownItems)
    {
        if (!Visible)
            return;

        if (shownItems <= 0)
        {
            SelectedIndex = -1;
            return;
        }

        SelectedIndex = Math.Clamp(SelectedIndex, 0, shownItems - 1);
    }

    public bool TrySelectPrev()
    {
        if (!Visible || Items.Length <= 0)
            return false;

        if (SelectedIndex < 0)
            SelectedIndex = 0;

        SelectedIndex = Math.Max(0, SelectedIndex - 1);
        return true;
    }

    public bool TrySelectNext()
    {
        if (!Visible || Items.Length <= 0)
            return false;

        if (SelectedIndex < 0)
            SelectedIndex = 0;

        SelectedIndex = Math.Min(Items.Length - 1, SelectedIndex + 1);
        return true;
    }

    public void TryBuy(CancellationToken token) => TrySendAction(Grobal2.CM_AFFIRMYBDEAL, "CM_AFFIRMYBDEAL", MirYbDealDialogMode.Deal, token);

    public void TryCancelDeal(CancellationToken token) => TrySendAction(Grobal2.CM_CANCELYBDEAL, "CM_CANCELYBDEAL", MirYbDealDialogMode.Deal, token);

    public void TryCancelSell(CancellationToken token) => TrySendAction(Grobal2.CM_CANCELYBSELL, "CM_CANCELYBSELL", MirYbDealDialogMode.Sell, token);

    public void TryCancelOrCancelSell(CancellationToken token)
    {
        if (Mode == MirYbDealDialogMode.Deal)
            TryCancelDeal(token);
        else
            TryCancelSell(token);
    }

    private void TrySendAction(ushort ident, string label, MirYbDealDialogMode requireMode, CancellationToken token)
    {
        if (!Visible || Mode != requireMode)
            return;

        int merchantId = _world.CurrentMerchantId;
        if (merchantId <= 0)
        {
            _log?.Invoke("[yb] action ignored: merchant not set");
            return;
        }

        long nowMs = Environment.TickCount64;
        if (nowMs - _lastActionMs < 250)
            return;
        _lastActionMs = nowMs;

        Close(logUi: true);

        _ = _session.SendClientMessageAsync(ident, merchantId, 0, 0, 0, token)
            .ContinueWith(
                t => _log?.Invoke($"[yb] {label} send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke($"[yb] {label} merchant={merchantId}");
    }
}

