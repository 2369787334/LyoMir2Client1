using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public sealed class BindDialogSystem
{
    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    private long _lastActionMs;

    public BindDialogSystem(MirClientSession session, MirWorldState world, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public bool Visible { get; private set; }
    public int MerchantId { get; private set; }
    public bool Unbind { get; private set; }
    public ClientItem SelectedItem { get; private set; }
    public int SelectedMakeIndex => SelectedItem.MakeIndex;
    public bool Waiting { get; private set; }
    public ClientItem WaitingItem { get; private set; }
    public long LastSendMs { get; private set; }

    public void Open(int merchantId, bool unbind, bool logUi)
    {
        Close(restoreSelectedItem: true, logUi: false);

        Visible = true;
        MerchantId = merchantId;
        Unbind = unbind;
        SelectedItem = default;
        LastSendMs = 0;
        _lastActionMs = 0;

        if (logUi)
        {
            _log?.Invoke(unbind
                ? $"[ui] unbind dialog opened merchant={merchantId}"
                : $"[ui] bind dialog opened merchant={merchantId}");
        }
    }

    public void Close(bool restoreSelectedItem, bool logUi)
    {
        if (restoreSelectedItem)
            RestoreSelectedToBag();
        else
            SelectedItem = default;

        Visible = false;
        MerchantId = 0;
        Unbind = false;
        LastSendMs = 0;
        _lastActionMs = 0;

        if (logUi)
            _log?.Invoke("[ui] bind dialog closed");
    }

    public void Reset()
    {
        Visible = false;
        MerchantId = 0;
        Unbind = false;
        SelectedItem = default;
        LastSendMs = 0;
        _lastActionMs = 0;
        Waiting = false;
        WaitingItem = default;
    }

    private void RestoreSelectedToBag()
    {
        ClientItem selected = SelectedItem;
        if (selected.MakeIndex == 0)
            return;

        _world.RestoreBagItem(selected);
        SelectedItem = default;
    }

    public bool HandleBagClick(int slotIndex, ClientItem clicked, bool logUi)
    {
        if (!Visible)
            return false;

        if (Waiting)
            return true;

        if (clicked.MakeIndex == 0)
            return false;

        if (SelectedItem.MakeIndex != 0 && SelectedItem.MakeIndex == clicked.MakeIndex)
            return true;

        RestoreSelectedToBag();

        ClientItem selected = clicked;
        if (_world.TryRemoveBagItemByMakeIndex(clicked.MakeIndex, out ClientItem removed))
            selected = removed;

        SelectedItem = selected;

        if (logUi)
            _log?.Invoke($"[bind] selected slot={slotIndex} makeIndex={SelectedItem.MakeIndex} '{SelectedItem.NameString}'");

        return true;
    }

    public void TrySendSelect(CancellationToken token)
    {
        if (!Visible)
            return;

        if (MerchantId <= 0)
        {
            _log?.Invoke("[bind] send ignored: merchant not set");
            return;
        }

        if (Waiting)
        {
            _log?.Invoke("[bind] send ignored: waiting server");
            return;
        }

        if (SelectedItem.MakeIndex == 0)
        {
            _log?.Invoke("[bind] send ignored: no item selected");
            return;
        }

        long nowMs = Environment.TickCount64;
        if (nowMs - _lastActionMs < 250)
            return;
        _lastActionMs = nowMs;
        LastSendMs = nowMs;

        ClientItem selected = SelectedItem;
        SelectedItem = default;

        Waiting = true;
        WaitingItem = selected;

        int makeIndex = selected.MakeIndex;
        ushort lo = unchecked((ushort)(makeIndex & 0xFFFF));
        ushort hi = unchecked((ushort)((makeIndex >> 16) & 0xFFFF));
        ushort idx = (ushort)(Unbind ? 1 : 0);
        string name = selected.NameString;

        _ = _session.SendClientStringAsync(Grobal2.CM_QUERYBINDITEM, MerchantId, lo, hi, idx, name, token)
            .ContinueWith(
                t => _log?.Invoke($"[bind] CM_QUERYBINDITEM send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke(Unbind
            ? $"[bind] CM_QUERYBINDITEM(unbind) merchant={MerchantId} makeIndex={makeIndex} '{name}'"
            : $"[bind] CM_QUERYBINDITEM(bind) merchant={MerchantId} makeIndex={makeIndex} '{name}'");
    }

    public void HandleServerResult(int code, bool logUi)
    {
        if (!Waiting || WaitingItem.MakeIndex == 0)
            return;

        ClientItem item = WaitingItem;
        Waiting = false;
        WaitingItem = default;
        LastSendMs = 0;

        if (code >= 0)
            item.S.Binded = (byte)(code != 0 ? 1 : 0);

        _world.RestoreBagItem(item);

        if (logUi)
            _log?.Invoke($"[bind] result code={code} restored makeIndex={item.MakeIndex} bound={(item.S.Binded != 0 ? 1 : 0)}");
    }
}

