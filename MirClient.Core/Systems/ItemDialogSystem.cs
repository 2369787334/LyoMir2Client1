using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public sealed class ItemDialogSystem
{
    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    private long _lastActionMs;

    public ItemDialogSystem(MirClientSession session, MirWorldState world, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public bool Visible { get; private set; }
    public int MerchantId { get; private set; }
    public string Prompt { get; private set; } = string.Empty;
    public ClientItem SelectedItem { get; private set; }
    public int SelectedMakeIndex => SelectedItem.MakeIndex;
    public long LastSendMs { get; private set; }

    public void Open(int merchantId, string? prompt, bool logUi)
    {
        Close(restoreSelectedItem: true, logUi: false);

        Visible = true;
        MerchantId = merchantId;
        Prompt = prompt ?? string.Empty;
        SelectedItem = default;
        LastSendMs = 0;
        _lastActionMs = 0;

        if (logUi)
            _log?.Invoke($"[ui] item dialog opened merchant={merchantId} prompt='{Prompt}'");
    }

    public void Close(bool restoreSelectedItem, bool logUi)
    {
        if (restoreSelectedItem)
            RestoreSelectedToBag();
        else
            SelectedItem = default;

        Visible = false;
        MerchantId = 0;
        Prompt = string.Empty;
        LastSendMs = 0;
        _lastActionMs = 0;

        if (logUi)
            _log?.Invoke("[ui] item dialog closed");
    }

    public void ClearSelectionWithoutRestore() => SelectedItem = default;

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
            _log?.Invoke($"[itemdlg] selected slot={slotIndex} makeIndex={SelectedItem.MakeIndex} '{SelectedItem.NameString}'");

        return true;
    }

    public void TrySendSelect(CancellationToken token)
    {
        if (!Visible)
            return;

        if (MerchantId <= 0)
        {
            _log?.Invoke("[itemdlg] send ignored: merchant not set");
            return;
        }

        if (SelectedItem.MakeIndex == 0)
        {
            _log?.Invoke("[itemdlg] send ignored: no item selected");
            return;
        }

        long nowMs = Environment.TickCount64;
        if (nowMs - _lastActionMs < 250)
            return;
        _lastActionMs = nowMs;
        LastSendMs = nowMs;

        int makeIndex = SelectedItem.MakeIndex;
        ushort lo = unchecked((ushort)(makeIndex & 0xFFFF));
        ushort hi = unchecked((ushort)((makeIndex >> 16) & 0xFFFF));
        string name = SelectedItem.NameString;

        _ = _session.SendClientStringAsync(Grobal2.CM_ITEMDLGSELECT, MerchantId, lo, hi, 0, name, token)
            .ContinueWith(
                t => _log?.Invoke($"[itemdlg] CM_ITEMDLGSELECT send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke($"[itemdlg] CM_ITEMDLGSELECT merchant={MerchantId} makeIndex={makeIndex} '{name}'");
    }

    public void HandleServerSelect(int recog, ushort param, bool logUi)
    {
        if (!Visible)
            return;

        if (param == 255)
            ClearSelectionWithoutRestore();

        if (recog == 0)
            Close(restoreSelectedItem: true, logUi: logUi);
    }
}

