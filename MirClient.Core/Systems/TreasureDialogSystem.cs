using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public enum MirTreasureDialogMode
{
    Identify = 1,
    Exchange = 2,
}

public sealed class TreasureDialogSystem
{
    private readonly MirClientSession _session;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    private long _lastActionMs;

    public TreasureDialogSystem(MirClientSession session, MirWorldState world, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public bool Visible { get; private set; }
    public MirTreasureDialogMode Mode { get; private set; } = MirTreasureDialogMode.Identify;

    public int SelectedMakeIndex0 { get; private set; }
    public int SelectedMakeIndex1 { get; private set; }
    public int NextSelectSlot { get; private set; }

    public long LastSendMs { get; private set; }
    public string Status { get; private set; } = string.Empty;

    public void Open(bool logUi)
    {
        Visible = true;
        Mode = MirTreasureDialogMode.Identify;
        SelectedMakeIndex0 = 0;
        SelectedMakeIndex1 = 0;
        NextSelectSlot = 0;
        LastSendMs = 0;
        Status = string.Empty;
        _lastActionMs = 0;

        if (logUi)
            _log?.Invoke("[ui] treasure dialog opened");
    }

    public void Close(bool logUi)
    {
        Visible = false;
        SelectedMakeIndex0 = 0;
        SelectedMakeIndex1 = 0;
        NextSelectSlot = 0;
        LastSendMs = 0;
        Status = string.Empty;
        _lastActionMs = 0;

        if (logUi)
            _log?.Invoke("[ui] treasure dialog closed");
    }

    public void Toggle(bool logUi)
    {
        if (Visible)
            Close(logUi);
        else
            Open(logUi);
    }

    public void ClearSelection(bool logUi)
    {
        SelectedMakeIndex0 = 0;
        SelectedMakeIndex1 = 0;
        NextSelectSlot = 0;
        Status = string.Empty;

        if (logUi)
            _log?.Invoke("[ti] selection cleared");
    }

    public void SetMode(MirTreasureDialogMode mode, bool logUi)
    {
        if (Mode == mode && Visible)
            return;

        Mode = mode;
        ClearSelection(logUi: false);

        if (logUi)
            _log?.Invoke($"[ui] treasure mode: {mode}");
    }

    public void ToggleMode(bool logUi)
    {
        SetMode(Mode == MirTreasureDialogMode.Identify ? MirTreasureDialogMode.Exchange : MirTreasureDialogMode.Identify, logUi);
    }

    public bool HandleBagClick(int slotIndex, ClientItem clicked, bool logUi)
    {
        if (!Visible)
            return false;

        if (clicked.MakeIndex == 0)
            return false;

        if (NextSelectSlot == 0)
            SelectedMakeIndex0 = clicked.MakeIndex;
        else
            SelectedMakeIndex1 = clicked.MakeIndex;

        NextSelectSlot ^= 1;
        Status = string.Empty;

        if (logUi)
            _log?.Invoke($"[ti] select bagSlot={slotIndex} makeIndex={clicked.MakeIndex} '{clicked.NameString}'");

        return true;
    }

    public void TrySendPrimary(CancellationToken token)
    {
        if (!Visible)
            return;

        if (Mode == MirTreasureDialogMode.Exchange)
            TrySendExchangeItem(token);
        else
            TrySendTreasureIdentify(series: 0, token);
    }

    public void TrySendSecondary(CancellationToken token)
    {
        if (!Visible)
            return;

        if (Mode == MirTreasureDialogMode.Exchange)
        {
            Status = "Secondary action not available.";
            return;
        }

        TrySendTreasureIdentify(series: 1, token);
    }

    private void TrySendTreasureIdentify(ushort series, CancellationToken token)
    {
        if (SelectedMakeIndex0 == 0 || SelectedMakeIndex1 == 0)
        {
            Status = "Select 2 items first.";
            return;
        }

        if (SelectedMakeIndex0 == SelectedMakeIndex1)
        {
            Status = "Invalid selection: same item.";
            return;
        }

        if (!TryGetBagItem(SelectedMakeIndex0, out ClientItem item0) ||
            !TryGetBagItem(SelectedMakeIndex1, out ClientItem item1))
        {
            Status = "Selected item missing in bag.";
            return;
        }

        
        if (item1.S.StdMode != 56 && item0.S.StdMode == 56)
            SwapSelection(ref item0, ref item1);

        if (item1.S.StdMode != 56)
        {
            Status = "Identify requires a book (StdMode=56).";
            return;
        }

        long nowMs = Environment.TickCount64;
        if (nowMs - _lastActionMs < 250)
            return;
        _lastActionMs = nowMs;
        LastSendMs = nowMs;

        int mainMakeIndex = item0.MakeIndex;
        int bookMakeIndex = item1.MakeIndex;
        ushort lo = unchecked((ushort)(bookMakeIndex & 0xFFFF));
        ushort hi = unchecked((ushort)((bookMakeIndex >> 16) & 0xFFFF));

        _ = _session.SendClientMessageAsync(Grobal2.CM_TreasureIdentify, mainMakeIndex, lo, hi, series, token)
            .ContinueWith(
                t => _log?.Invoke($"[ti] CM_TreasureIdentify send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        Status = series == 0 ? "Sent: TreasureIdentify (normal)" : "Sent: TreasureIdentify (special)";
        _log?.Invoke($"[ti] CM_TreasureIdentify main={mainMakeIndex} book={bookMakeIndex} series={series}");
    }

    private void TrySendExchangeItem(CancellationToken token)
    {
        if (SelectedMakeIndex0 == 0 || SelectedMakeIndex1 == 0)
        {
            Status = "Select 2 items first.";
            return;
        }

        if (SelectedMakeIndex0 == SelectedMakeIndex1)
        {
            Status = "Invalid selection: same item.";
            return;
        }

        if (!TryGetBagItem(SelectedMakeIndex0, out ClientItem item0) ||
            !TryGetBagItem(SelectedMakeIndex1, out ClientItem item1))
        {
            Status = "Selected item missing in bag.";
            return;
        }

        
        if (!IsExchangeCharm(item1) && IsExchangeCharm(item0))
            SwapSelection(ref item0, ref item1);

        if (item0.S.Eva.EvaTimes == 0)
        {
            Status = "Exchange requires EvaTimes>0 on item0.";
            return;
        }

        if (!IsExchangeCharm(item1))
        {
            Status = "Exchange requires a charm (StdMode=41 Shape=30).";
            return;
        }

        long nowMs = Environment.TickCount64;
        if (nowMs - _lastActionMs < 250)
            return;
        _lastActionMs = nowMs;
        LastSendMs = nowMs;

        int mainMakeIndex = item0.MakeIndex;
        int charmMakeIndex = item1.MakeIndex;
        ushort lo = unchecked((ushort)(charmMakeIndex & 0xFFFF));
        ushort hi = unchecked((ushort)((charmMakeIndex >> 16) & 0xFFFF));

        _ = _session.SendClientMessageAsync(Grobal2.CM_ExchangeItem, mainMakeIndex, lo, hi, 0, token)
            .ContinueWith(
                t => _log?.Invoke($"[ti] CM_ExchangeItem send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        Status = "Sent: ExchangeItem";
        _log?.Invoke($"[ti] CM_ExchangeItem main={mainMakeIndex} charm={charmMakeIndex}");
    }

    private bool TryGetBagItem(int makeIndex, out ClientItem item)
    {
        item = default;

        if (makeIndex == 0)
            return false;

        foreach (ClientItem slot in _world.BagSlots)
        {
            if (slot.MakeIndex != makeIndex)
                continue;

            item = slot;
            return true;
        }

        return false;
    }

    private bool IsExchangeCharm(in ClientItem item) => item.S.StdMode == 41 && item.S.Shape == 30;

    private void SwapSelection(ref ClientItem item0, ref ClientItem item1)
    {
        (SelectedMakeIndex0, SelectedMakeIndex1) = (SelectedMakeIndex1, SelectedMakeIndex0);
        (item0, item1) = (item1, item0);
    }
}

