using System.Runtime.InteropServices;
using MirClient.Core.World;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public sealed class StallSystem
{
    private const long ActionCooldownMs = 120;
    private const long PendingTimeoutMs = 10_000;
    private const long PendingExpireMs = 15_000;

    private enum PendingAction
    {
        None = 0,
        Add = 1,
        Remove = 2
    }

    private readonly MaketSystem _maketSystem;
    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    private long _lastActionMs;

    private PendingAction _pendingAction;
    private int _pendingMakeIndex;
    private int _pendingSlotIndex = -1;
    private StallSlot _pendingSlot;
    private long _pendingSinceMs;

    private readonly StallSlot[] _slots = new StallSlot[ClientStallItems.MaxStallItemCount];

    public bool WindowVisible { get; private set; }
    public int SelectedIndex { get; private set; } = -1;
    public string Name { get; private set; } = string.Empty;

    public StallSlot[] Slots => _slots;

    public StallSystem(MaketSystem maketSystem, MirWorldState world, Action<string>? log = null)
    {
        _maketSystem = maketSystem ?? throw new ArgumentNullException(nameof(maketSystem));
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public void ToggleWindow(bool logUi)
    {
        WindowVisible = !WindowVisible;
        SelectedIndex = -1;

        if (logUi)
            _log?.Invoke(WindowVisible ? "[ui] stall opened" : "[ui] stall closed");
    }

    public void CloseWindow(bool logUi)
    {
        WindowVisible = false;
        SelectedIndex = -1;

        if (logUi)
            _log?.Invoke("[ui] stall closed");
    }

    public void ClearState(bool resetBagFlags)
    {
        ClearPending();

        if (resetBagFlags)
            ResetBagStallFlags();

        Array.Clear(_slots);
        SelectedIndex = -1;
        Name = string.Empty;
    }

    public void SetNameFromUi(string name, bool logUi)
    {
        Name = name ?? string.Empty;

        if (logUi)
            _log?.Invoke($"[stall] name='{Name}'");
    }

    public void SelectSlot(int index, StallSlot slot, bool logUi)
    {
        SelectedIndex = index;

        if (logUi && slot.HasItem)
            _log?.Invoke($"[stall] select idx={index} name='{slot.Item.NameString}' price={slot.Price} type={slot.GoldType}");
    }

    public int CountSlots()
    {
        int count = 0;
        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].HasItem)
                count++;
        }

        return count;
    }

    public bool IsMyStallOnSale()
    {
        return _world.MyselfRecogIdSet &&
               _world.MyselfRecogId != 0 &&
               _world.Stalls.ContainsKey(_world.MyselfRecogId);
    }

    public bool TryBuildOpenRequest(out ClientStallItems stallItems, out int count)
    {
        stallItems = default;
        count = 0;

        stallItems.SetNameString(Name);

        for (int i = 0; i < _slots.Length; i++)
        {
            StallSlot slot = _slots[i];
            if (!slot.HasItem)
                continue;

            if (count >= ClientStallItems.MaxStallItemCount)
                break;

            stallItems.SetItem(
                count,
                new ClientStall
                {
                    MakeIndex = slot.Item.MakeIndex,
                    Price = slot.Price,
                    GoldType = slot.GoldType
                });
            count++;
        }

        return count > 0;
    }

    public async ValueTask<bool> TrySendOpenAsync(CancellationToken token)
    {
        if (!_world.MyselfRecogIdSet || _world.MyselfRecogId == 0)
            return false;

        if (IsMyStallOnSale())
            return false;

        if (string.IsNullOrWhiteSpace(Name))
            return false;

        if (!TryBuildOpenRequest(out ClientStallItems req, out int count) || count <= 0)
            return false;

        long nowMs = Environment.TickCount64;
        if (!TryBeginAction(nowMs))
            return false;

        if (await _maketSystem.TrySendOpenStallAsync(count, req, token))
        {
            WindowVisible = false;
            SelectedIndex = -1;
            return true;
        }

        return false;
    }

    public async ValueTask TrySendCloseAsync(CancellationToken token)
    {
        if (!IsMyStallOnSale())
            return;

        long nowMs = Environment.TickCount64;
        if (!TryBeginAction(nowMs))
            return;

        await _maketSystem.TrySendCloseStallAsync(token);
    }

    public async ValueTask TryRemoveSelectedAsync(CancellationToken token)
    {
        if ((uint)SelectedIndex >= (uint)_slots.Length)
            return;

        StallSlot selected = _slots[SelectedIndex];
        if (!selected.HasItem)
            return;

        int makeIndex = selected.Item.MakeIndex;
        bool onSale = IsMyStallOnSale();

        long nowMs = Environment.TickCount64;
        if (!TryBeginAction(nowMs))
            return;

        if (onSale)
        {
            if (IsUpdatePending(nowMs, clearOnTimeout: true))
            {
                _log?.Invoke($"[stall] remove ignored (pending {_pendingAction} makeIndex={_pendingMakeIndex})");
                return;
            }

            _pendingAction = PendingAction.Remove;
            _pendingMakeIndex = makeIndex;
            _pendingSlotIndex = SelectedIndex;
            _pendingSlot = selected;
            _pendingSinceMs = nowMs;

            if (!await _maketSystem.TrySendUpdateStallItemRemoveAsync(makeIndex, token))
            {
                ClearPending();
                return;
            }
        }

        _slots[SelectedIndex] = default;
        SelectedIndex = -1;
        _ = TrySetBagItemNeedIdentify(makeIndex, needIdentify: 0);
    }

    public async ValueTask TryAddItemAsync(ClientItem item, int price, byte goldType, int bagSlotIndex, CancellationToken token)
    {
        if (item.MakeIndex == 0)
            return;

        if (item.S.NeedIdentify >= 4)
        {
            _log?.Invoke($"[stall] add ignored (already stall makeIndex={item.MakeIndex} name='{item.NameString}')");
            return;
        }

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Item.MakeIndex == item.MakeIndex)
            {
                _log?.Invoke($"[stall] add ignored (duplicate makeIndex={item.MakeIndex} name='{item.NameString}')");
                return;
            }
        }

        if (!TryFindEmptySlot(out int emptyIndex))
        {
            _log?.Invoke("[stall] add ignored (stall full)");
            return;
        }

        bool onSale = IsMyStallOnSale();
        long nowMs = Environment.TickCount64;

        if (onSale)
        {
            if (IsUpdatePending(nowMs, clearOnTimeout: true))
            {
                _log?.Invoke($"[stall] add ignored (pending {_pendingAction} makeIndex={_pendingMakeIndex})");
                return;
            }

            if (!TryBeginAction(nowMs))
                return;
        }

        byte bagFlag = onSale ? (byte)5 : (byte)4;
        _ = TrySetBagItemNeedIdentify(item.MakeIndex, bagFlag);

        var slot = new StallSlot { Item = item, Price = price, GoldType = goldType };
        _slots[emptyIndex] = slot;
        SelectedIndex = emptyIndex;

        _log?.Invoke($"[stall] add idx={emptyIndex} slot={bagSlotIndex} name='{item.NameString}' price={price} type={goldType} sale={onSale}");

        if (!onSale)
            return;

        _pendingAction = PendingAction.Add;
        _pendingMakeIndex = item.MakeIndex;
        _pendingSlotIndex = emptyIndex;
        _pendingSlot = slot;
        _pendingSinceMs = nowMs;

        if (!await _maketSystem.TrySendUpdateStallItemAddAsync(item.MakeIndex, price, goldType, token))
        {
            _slots[emptyIndex] = default;
            SelectedIndex = -1;
            _ = TrySetBagItemNeedIdentify(item.MakeIndex, needIdentify: 0);
            ClearPending();
        }
    }

    public void HandleServerOpenStall(int actorId, StallActorMarker stall)
    {
        if (!_world.MyselfRecogIdSet || _world.MyselfRecogId == 0)
            return;

        if (actorId != _world.MyselfRecogId)
            return;

        if (stall.Open)
        {
            if (!string.IsNullOrWhiteSpace(stall.Name))
                Name = stall.Name.Trim();

            for (int i = 0; i < _slots.Length; i++)
            {
                int makeIndex = _slots[i].Item.MakeIndex;
                if (makeIndex != 0)
                    _ = TrySetBagItemNeedIdentify(makeIndex, needIdentify: 5);
            }

            return;
        }

        ClearState(resetBagFlags: true);
        WindowVisible = false;
        SelectedIndex = -1;
    }

    public void HandleUpdateStallItemResult(int code, string? text)
    {
        if (_pendingAction == PendingAction.None)
        {
            _log?.Invoke(string.IsNullOrWhiteSpace(text)
                ? $"[stall] SM_UPDATESTALLITEM code={code}"
                : $"[stall] SM_UPDATESTALLITEM code={code} '{text}'");
            return;
        }

        if (!IsMyStallOnSale())
        {
            _log?.Invoke($"[stall] update ignored (stall not on sale, pending={_pendingAction} makeIndex={_pendingMakeIndex} code={code})");
            ClearPending();
            return;
        }

        long nowMs = Environment.TickCount64;
        if (nowMs - _pendingSinceMs >= PendingExpireMs)
        {
            _log?.Invoke($"[stall] update ignored (pending expired action={_pendingAction} makeIndex={_pendingMakeIndex} code={code})");
            ClearPending();
            return;
        }

        PendingAction action = _pendingAction;
        int makeIndex = _pendingMakeIndex;
        int slotIndex = _pendingSlotIndex;
        StallSlot slot = _pendingSlot;

        if (code < 0)
        {
            if (action == PendingAction.Add)
            {
                RemoveSlotByMakeIndex(makeIndex);
                _ = TrySetBagItemNeedIdentify(makeIndex, needIdentify: 0);
                _log?.Invoke(string.IsNullOrWhiteSpace(text)
                    ? $"[stall] update failed (add reverted makeIndex={makeIndex} code={code})"
                    : $"[stall] update failed (add reverted makeIndex={makeIndex} code={code} '{text}')");
            }
            else if (action == PendingAction.Remove)
            {
                if (slot.HasItem)
                {
                    if (!StallHasItem(makeIndex))
                    {
                        if ((uint)slotIndex < (uint)_slots.Length && !_slots[slotIndex].HasItem)
                            _slots[slotIndex] = slot;
                        else if (TryFindEmptySlot(out int empty))
                            _slots[empty] = slot;
                    }

                    _ = TrySetBagItemNeedIdentify(makeIndex, needIdentify: 5);
                }

                _log?.Invoke(string.IsNullOrWhiteSpace(text)
                    ? $"[stall] update failed (remove reverted makeIndex={makeIndex} code={code})"
                    : $"[stall] update failed (remove reverted makeIndex={makeIndex} code={code} '{text}')");
            }

            ClearPending();
            return;
        }

        if (action == PendingAction.Add)
        {
            if (slot.HasItem)
            {
                if (!StallHasItem(makeIndex))
                {
                    if ((uint)slotIndex < (uint)_slots.Length && !_slots[slotIndex].HasItem)
                        _slots[slotIndex] = slot;
                    else if (TryFindEmptySlot(out int empty))
                        _slots[empty] = slot;
                }

                _ = TrySetBagItemNeedIdentify(makeIndex, needIdentify: 5);
            }

            _log?.Invoke(string.IsNullOrWhiteSpace(text)
                ? $"[stall] update ok (add makeIndex={makeIndex} code={code})"
                : $"[stall] update ok (add makeIndex={makeIndex} code={code} '{text}')");
        }
        else if (action == PendingAction.Remove)
        {
            RemoveSlotByMakeIndex(makeIndex);
            _ = TrySetBagItemNeedIdentify(makeIndex, needIdentify: 0);
            _log?.Invoke(string.IsNullOrWhiteSpace(text)
                ? $"[stall] update ok (remove makeIndex={makeIndex} code={code})"
                : $"[stall] update ok (remove makeIndex={makeIndex} code={code} '{text}')");
        }

        ClearPending();
    }

    public void HandleBuyStallItemResult(int code, string? text)
    {
        _log?.Invoke(string.IsNullOrWhiteSpace(text) ? $"[stall] SM_BUYSTALLITEM code={code}" : $"[stall] SM_BUYSTALLITEM code={code} '{text}'");
    }

    private void ClearPending()
    {
        _pendingAction = PendingAction.None;
        _pendingMakeIndex = 0;
        _pendingSlotIndex = -1;
        _pendingSlot = default;
        _pendingSinceMs = 0;
    }

    private bool TryBeginAction(long nowMs)
    {
        if (nowMs - _lastActionMs < ActionCooldownMs)
            return false;

        _lastActionMs = nowMs;
        return true;
    }

    private bool IsUpdatePending(long nowMs, bool clearOnTimeout)
    {
        if (_pendingAction == PendingAction.None)
            return false;

        if (nowMs - _pendingSinceMs < PendingTimeoutMs)
            return true;

        if (clearOnTimeout)
        {
            _log?.Invoke($"[stall] pending cleared (timeout action={_pendingAction} makeIndex={_pendingMakeIndex})");
            ClearPending();
        }

        return false;
    }

    private bool StallHasItem(int makeIndex)
    {
        if (makeIndex == 0)
            return false;

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Item.MakeIndex == makeIndex)
                return true;
        }

        return false;
    }

    private void RemoveSlotByMakeIndex(int makeIndex)
    {
        if (makeIndex == 0)
            return;

        for (int i = 0; i < _slots.Length; i++)
        {
            if (_slots[i].Item.MakeIndex != makeIndex)
                continue;

            _slots[i] = default;
            if (SelectedIndex == i)
                SelectedIndex = -1;
            return;
        }
    }

    private bool TryFindEmptySlot(out int emptyIndex)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            if (!_slots[i].HasItem)
            {
                emptyIndex = i;
                return true;
            }
        }

        emptyIndex = -1;
        return false;
    }

    private void ResetBagStallFlags()
    {
        ReadOnlySpan<ClientItem> bagSlots = _world.BagSlots;
        for (int i = 0; i < bagSlots.Length; i++)
        {
            ClientItem item = bagSlots[i];
            if (item.MakeIndex == 0)
                continue;

            if (item.S.NeedIdentify < 4)
                continue;

            ClientItem updated = item;
            updated.S.NeedIdentify = 0;
            _ = _world.TryApplyUpdateBagItem(EncodeClientItem(updated), out _);
        }
    }

    private bool TrySetBagItemNeedIdentify(int makeIndex, byte needIdentify)
    {
        if (makeIndex == 0)
            return false;

        ReadOnlySpan<ClientItem> bagSlots = _world.BagSlots;
        for (int i = 0; i < bagSlots.Length; i++)
        {
            ClientItem item = bagSlots[i];
            if (item.MakeIndex != makeIndex)
                continue;

            ClientItem updated = item;
            updated.S.NeedIdentify = needIdentify;
            return _world.TryApplyUpdateBagItem(EncodeClientItem(updated), out _);
        }

        return false;
    }

    private static string EncodeClientItem(ClientItem item)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref item, 1));
        return EdCode.EncodeBuffer(bytes);
    }
}

