using System.Runtime.InteropServices;
using MirClient.Core.Messages;
using MirClient.Core.World;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Systems;

public sealed class InventoryPendingSystem
{
    public const long DefaultTimeoutMs = 10_000;

    private readonly MirWorldState _world;
    private readonly Action<string>? _log;

    private bool _useItemPending;
    private long _useItemSinceMs;
    private int _useItemMakeIndex;
    private int _useItemWhere = -1;
    private bool _useItemHero;
    private bool _useItemTakeOff;
    private ClientItem _useItemItem;

    private bool _eatPending;
    private long _eatSinceMs;
    private bool _eatHero;
    private int _eatSlotIndex = -1;
    private ClientItem _eatItem;

    private bool _dropPending;
    private long _dropSinceMs;
    private int _dropMakeIndex;

    private bool _heroBagExchangePending;
    private long _heroBagExchangeSinceMs;
    private bool _heroBagExchangeHeroToPlayer;
    private ClientItem _heroBagExchangeItem;

    private bool _itemSumCountPending;
    private long _itemSumCountSinceMs;
    private bool _itemSumCountHero;
    private int _itemSumCountOrgMakeIndex;
    private int _itemSumCountExMakeIndex;
    private ClientItem _itemSumCountItem;

    public InventoryPendingSystem(MirWorldState world, Action<string>? log = null)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
        _log = log;
    }

    public int UseItemMakeIndex => _useItemMakeIndex;
    public int UseItemWhere => _useItemWhere;

    public bool IsUseItemPendingActive(long nowMs) =>
        _useItemPending && _useItemMakeIndex != 0 && nowMs - _useItemSinceMs < DefaultTimeoutMs;

    public MirEquipmentMessageHandlers.UseItemPending? GetUseItemPending()
    {
        if (!_useItemPending || _useItemItem.MakeIndex == 0 || _useItemWhere < 0)
            return null;

        return new MirEquipmentMessageHandlers.UseItemPending(true, _useItemHero, _useItemTakeOff, _useItemWhere, _useItemItem);
    }

    public void SetUseItemPending(bool hero, bool takeOff, ClientItem item, int where, long nowMs)
    {
        _useItemPending = true;
        _useItemSinceMs = nowMs;
        _useItemMakeIndex = item.MakeIndex;
        _useItemWhere = where;
        _useItemHero = hero;
        _useItemTakeOff = takeOff;
        _useItemItem = item;
    }

    public void ClearUseItemPending()
    {
        _useItemPending = false;
        _useItemSinceMs = 0;
        _useItemMakeIndex = 0;
        _useItemWhere = -1;
        _useItemHero = false;
        _useItemTakeOff = false;
        _useItemItem = default;
    }

    public int EatMakeIndex => _eatItem.MakeIndex;

    public bool IsEatPendingActive(long nowMs) =>
        _eatPending && _eatItem.MakeIndex != 0 && nowMs - _eatSinceMs < DefaultTimeoutMs;

    public MirEquipmentMessageHandlers.EatPending? GetEatPending()
    {
        if (!_eatPending || _eatItem.MakeIndex == 0)
            return null;

        return new MirEquipmentMessageHandlers.EatPending(true, _eatHero, _eatItem, _eatSlotIndex);
    }

    public void SetEatPending(bool hero, ClientItem item, int slotIndex, long nowMs)
    {
        _eatPending = true;
        _eatSinceMs = nowMs;
        _eatHero = hero;
        _eatSlotIndex = slotIndex;
        _eatItem = item;
    }

    public void ClearEatPending()
    {
        _eatPending = false;
        _eatSinceMs = 0;
        _eatHero = false;
        _eatSlotIndex = -1;
        _eatItem = default;
    }

    public void RestoreEatPendingToBag(ushort? updatedDura = null)
    {
        if (!_eatPending || _eatItem.MakeIndex == 0)
        {
            ClearEatPending();
            return;
        }

        ClientItem item = _eatItem;
        if (updatedDura is { } dura && dura > 0)
            item.Dura = dura;

        if (_eatHero)
            _world.RestoreHeroBagItem(item);
        else
            _world.RestoreBagItem(item);

        ClearEatPending();
    }

    public int DropMakeIndex => _dropMakeIndex;

    public bool IsDropPendingActive(long nowMs) =>
        _dropPending && _dropMakeIndex != 0 && nowMs - _dropSinceMs < DefaultTimeoutMs;

    public void SetDropPending(int makeIndex, long nowMs)
    {
        _dropPending = true;
        _dropSinceMs = nowMs;
        _dropMakeIndex = makeIndex;
    }

    public void ClearDropPending()
    {
        _dropPending = false;
        _dropSinceMs = 0;
        _dropMakeIndex = 0;
    }

    public int HeroBagExchangeMakeIndex => _heroBagExchangeItem.MakeIndex;
    public bool HeroBagExchangeHeroToPlayer => _heroBagExchangeHeroToPlayer;

    public string HeroBagExchangeDirectionLabel => _heroBagExchangeHeroToPlayer ? "HeroToPlayer" : "PlayerToHero";

    public bool IsHeroBagExchangePendingActive(long nowMs) =>
        _heroBagExchangePending && _heroBagExchangeItem.MakeIndex != 0 && nowMs - _heroBagExchangeSinceMs < DefaultTimeoutMs;

    public void SetHeroBagExchangePending(bool heroToPlayer, ClientItem item, long nowMs)
    {
        _heroBagExchangePending = true;
        _heroBagExchangeSinceMs = nowMs;
        _heroBagExchangeHeroToPlayer = heroToPlayer;
        _heroBagExchangeItem = item;
    }

    public void ClearHeroBagExchangePending()
    {
        _heroBagExchangePending = false;
        _heroBagExchangeSinceMs = 0;
        _heroBagExchangeHeroToPlayer = false;
        _heroBagExchangeItem = default;
    }

    public MirInventoryMessageHandlers.HeroBagExchangePending? GetHeroBagExchangePending()
    {
        if (!_heroBagExchangePending || _heroBagExchangeItem.MakeIndex == 0)
            return null;

        return new MirInventoryMessageHandlers.HeroBagExchangePending(true, _heroBagExchangeHeroToPlayer, _heroBagExchangeItem);
    }

    public int ItemSumCountMakeIndex => _itemSumCountItem.MakeIndex;

    public bool IsItemSumCountPendingActive(long nowMs) =>
        _itemSumCountPending && _itemSumCountItem.MakeIndex != 0 && nowMs - _itemSumCountSinceMs < DefaultTimeoutMs;

    public MirMerchantMessageHandlers.ItemSumCountPending? GetItemSumCountPending()
    {
        if (!_itemSumCountPending || _itemSumCountItem.MakeIndex == 0)
            return null;

        return new MirMerchantMessageHandlers.ItemSumCountPending(
            true,
            _itemSumCountHero,
            _itemSumCountOrgMakeIndex,
            _itemSumCountExMakeIndex,
            _itemSumCountItem);
    }

    public void SetItemSumCountPending(bool hero, int orgMakeIndex, int exMakeIndex, ClientItem item, long nowMs)
    {
        _itemSumCountPending = true;
        _itemSumCountSinceMs = nowMs;
        _itemSumCountHero = hero;
        _itemSumCountOrgMakeIndex = orgMakeIndex;
        _itemSumCountExMakeIndex = exMakeIndex;
        _itemSumCountItem = item;
    }

    public void ClearItemSumCountPending()
    {
        _itemSumCountPending = false;
        _itemSumCountSinceMs = 0;
        _itemSumCountHero = false;
        _itemSumCountOrgMakeIndex = 0;
        _itemSumCountExMakeIndex = 0;
        _itemSumCountItem = default;
    }

    public void RestoreItemSumCountToBag()
    {
        if (!_itemSumCountPending || _itemSumCountItem.MakeIndex == 0)
        {
            ClearItemSumCountPending();
            return;
        }

        ClientItem item = _itemSumCountItem;

        if (_itemSumCountHero)
        {
            if (!_world.HeroBagItems.ContainsKey(item.MakeIndex))
                _ = _world.TryApplyHeroAddBagItem(EncodeClientItem(item), out _);
        }
        else
        {
            if (!_world.BagItems.ContainsKey(item.MakeIndex))
                _ = _world.TryApplyAddBagItem(EncodeClientItem(item), out _);
        }

        ClearItemSumCountPending();
    }

    public void Tick(long nowMs)
    {
        if (_eatPending && _eatItem.MakeIndex != 0 && nowMs - _eatSinceMs >= DefaultTimeoutMs)
        {
            int makeIndex = _eatItem.MakeIndex;
            _log?.Invoke($"[item] pending eat cleared (timeout makeIndex={makeIndex} hero={_eatHero} slot={_eatSlotIndex})");
            RestoreEatPendingToBag();
        }

        if (!_itemSumCountPending || _itemSumCountItem.MakeIndex == 0)
            return;

        if (nowMs - _itemSumCountSinceMs < DefaultTimeoutMs)
            return;

        int itemSumMakeIndex = _itemSumCountItem.MakeIndex;
        _log?.Invoke($"[item] pending item sum count cleared (timeout makeIndex={itemSumMakeIndex})");
        RestoreItemSumCountToBag();
    }

    private static string EncodeClientItem(ClientItem item)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref item, 1));
        return EdCode.EncodeBuffer(bytes);
    }
}
