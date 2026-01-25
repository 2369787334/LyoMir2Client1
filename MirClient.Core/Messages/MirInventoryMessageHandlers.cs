using System.Runtime.InteropServices;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirInventoryMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action? onBagItemsApplied = null,
        Action<string, string>? onChat = null,
        Action? onDropResponse = null,
        Action<ClientItem>? onBagItemAdded = null,
        Func<HeroBagExchangePending?>? getHeroBagExchangePending = null,
        Action? clearHeroBagExchangePending = null,
        Func<MirMerchantMessageHandlers.ItemSumCountPending?>? getItemSumCountPending = null,
        Action? clearItemSumCountPending = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_BAGITEMS, packet =>
        {
            MirBagItemsUpdate update = world.ApplyBagItems(packet.BodyEncoded);
            onBagItemsApplied?.Invoke();
            log?.Invoke(!string.IsNullOrWhiteSpace(update.SampleNames)
                ? $"[bag] SM_BAGITEMS count={update.Count} sample={update.SampleNames}"
                : $"[bag] SM_BAGITEMS count={update.Count}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROBAGITEMS, packet =>
        {
            MirBagItemsUpdate update = world.ApplyHeroBagItems(packet.BodyEncoded, packet.Header.Series);
            int bagSize = world.HeroBagSize;
            log?.Invoke(!string.IsNullOrWhiteSpace(update.SampleNames)
                ? $"[hero-bag] SM_HEROBAGITEMS size={bagSize} count={update.Count} sample={update.SampleNames}"
                : $"[hero-bag] SM_HEROBAGITEMS size={bagSize} count={update.Count}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_ADDITEM, packet =>
        {
            if (!world.TryApplyAddBagItem(packet.BodyEncoded, out ClientItem item))
            {
                log?.Invoke($"[bag] SM_ADDITEM decode failed (len={packet.BodyEncoded.Length}).");
                return ValueTask.CompletedTask;
            }

            log?.Invoke($"[bag] SM_ADDITEM name='{item.NameString}' makeIndex={item.MakeIndex} dura={item.Dura}/{item.DuraMax} looks={item.S.Looks} overlap={item.S.Overlap}");
            onBagItemAdded?.Invoke(item);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROADDITEM, packet =>
        {
            if (!world.TryApplyHeroAddBagItem(packet.BodyEncoded, out ClientItem item))
            {
                log?.Invoke($"[hero-bag] SM_HEROADDITEM decode failed (len={packet.BodyEncoded.Length}).");
                return ValueTask.CompletedTask;
            }

            log?.Invoke($"[hero-bag] SM_HEROADDITEM name='{item.NameString}' makeIndex={item.MakeIndex} dura={item.Dura}/{item.DuraMax} looks={item.S.Looks} overlap={item.S.Overlap}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_UPDATEITEM, packet =>
        {
            if (!world.TryApplyUpdateBagItem(packet.BodyEncoded, out ClientItem item))
            {
                log?.Invoke($"[bag] SM_UPDATEITEM decode failed (len={packet.BodyEncoded.Length}).");
                return ValueTask.CompletedTask;
            }

            log?.Invoke($"[bag] SM_UPDATEITEM name='{item.NameString}' makeIndex={item.MakeIndex} dura={item.Dura}/{item.DuraMax} looks={item.S.Looks} overlap={item.S.Overlap}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROUPDATEITEM, packet =>
        {
            if (!world.TryApplyHeroUpdateBagItem(packet.BodyEncoded, out ClientItem item))
            {
                log?.Invoke($"[hero-bag] SM_HEROUPDATEITEM decode failed (len={packet.BodyEncoded.Length}).");
                return ValueTask.CompletedTask;
            }

            log?.Invoke($"[hero-bag] SM_HEROUPDATEITEM name='{item.NameString}' makeIndex={item.MakeIndex} dura={item.Dura}/{item.DuraMax} looks={item.S.Looks} overlap={item.S.Overlap}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DELITEM, packet =>
        {
            if (!world.TryApplyBagItemRemove(packet.BodyEncoded, out ClientItem item))
            {
                log?.Invoke($"[bag] SM_DELITEM decode failed (len={packet.BodyEncoded.Length}).");
                return ValueTask.CompletedTask;
            }

            log?.Invoke($"[bag] SM_DELITEM name='{item.NameString}' makeIndex={item.MakeIndex}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HERODELITEM, packet =>
        {
            if (!world.TryApplyHeroBagItemRemove(packet.BodyEncoded, out ClientItem item))
            {
                log?.Invoke($"[hero-bag] SM_HERODELITEM decode failed (len={packet.BodyEncoded.Length}).");
                return ValueTask.CompletedTask;
            }

            log?.Invoke($"[hero-bag] SM_HERODELITEM name='{item.NameString}' makeIndex={item.MakeIndex}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DELITEMS, packet =>
        {
            bool onlyBag = packet.Header.Param != 0;
            MirBagItemsUpdate update = world.ApplyDelItemList(packet.BodyEncoded, onlyBag);
            log?.Invoke(!string.IsNullOrWhiteSpace(update.SampleNames)
                ? $"[bag] SM_DELITEMS removed={update.Count} onlyBag={onlyBag} sample={update.SampleNames}"
                : $"[bag] SM_DELITEMS removed={update.Count} onlyBag={onlyBag}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HERODELITEMS, packet =>
        {
            bool onlyBag = packet.Header.Param != 0;
            MirBagItemsUpdate update = world.ApplyHeroDelItemList(packet.BodyEncoded, onlyBag);
            log?.Invoke(!string.IsNullOrWhiteSpace(update.SampleNames)
                ? $"[hero-bag] SM_HERODELITEMS removed={update.Count} onlyBag={onlyBag} sample={update.SampleNames}"
                : $"[hero-bag] SM_HERODELITEMS removed={update.Count} onlyBag={onlyBag}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_COUNTERITEMCHANGE, packet =>
        {
            int makeIndex = packet.Header.Recog;
            int count = packet.Header.Param;
            int msgNum = packet.Header.Tag;
            string name = EdCode.DecodeString(packet.BodyEncoded);

            bool ok = world.TryApplyCounterItemChange(makeIndex, count, msgNum, name, out _);
            log?.Invoke(ok
                ? $"[bag] SM_COUNTERITEMCHANGE name='{name}' makeIndex={makeIndex} count={count} msg={msgNum}"
                : $"[bag] SM_COUNTERITEMCHANGE not-found name='{name}' makeIndex={makeIndex} count={count} msg={msgNum}");

            if (getItemSumCountPending?.Invoke() is { Pending: true, Hero: false } pending &&
                pending.Item.MakeIndex != 0 &&
                (pending.OrgMakeIndex == makeIndex || pending.ExMakeIndex == makeIndex))
            {
                clearItemSumCountPending?.Invoke();
                log?.Invoke($"[item] pending item sum count cleared (SM_COUNTERITEMCHANGE makeIndex={makeIndex})");
            }
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROCOUNTERITEMCHANGE, packet =>
        {
            int makeIndex = packet.Header.Recog;
            int count = packet.Header.Param;
            int msgNum = packet.Header.Tag;
            string name = EdCode.DecodeString(packet.BodyEncoded);

            bool ok = world.TryApplyHeroCounterItemChange(makeIndex, count, msgNum, name, out _);
            log?.Invoke(ok
                ? $"[hero-bag] SM_HEROCOUNTERITEMCHANGE name='{name}' makeIndex={makeIndex} count={count} msg={msgNum}"
                : $"[hero-bag] SM_HEROCOUNTERITEMCHANGE not-found name='{name}' makeIndex={makeIndex} count={count} msg={msgNum}");

            if (getItemSumCountPending?.Invoke() is { Pending: true, Hero: true } pending &&
                pending.Item.MakeIndex != 0 &&
                (pending.OrgMakeIndex == makeIndex || pending.ExMakeIndex == makeIndex))
            {
                clearItemSumCountPending?.Invoke();
                log?.Invoke($"[item] pending item sum count cleared (SM_HEROCOUNTERITEMCHANGE makeIndex={makeIndex})");
            }
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DROPITEM_SUCCESS, packet =>
        {
            onDropResponse?.Invoke();
            string name = EdCode.DecodeString(packet.BodyEncoded);
            int makeIndex = packet.Header.Recog;
            bool removed = world.TryRemoveBagItemByMakeIndex(makeIndex, out ClientItem item);
            log?.Invoke(removed
                ? $"[bag] SM_DROPITEM_SUCCESS name='{name}' makeIndex={makeIndex} removed='{item.NameString}'"
                : $"[bag] SM_DROPITEM_SUCCESS name='{name}' makeIndex={makeIndex} (not found in bag)");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DROPITEM_FAIL, packet =>
        {
            onDropResponse?.Invoke();
            string name = EdCode.DecodeString(packet.BodyEncoded);
            log?.Invoke($"[bag] SM_DROPITEM_FAIL name='{name}' makeIndex={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HERODROPITEM_SUCCESS, packet =>
        {
            onDropResponse?.Invoke();
            string name = EdCode.DecodeString(packet.BodyEncoded);
            int makeIndex = packet.Header.Recog;
            bool removed = world.TryRemoveHeroBagItemByMakeIndex(makeIndex, out ClientItem item);
            log?.Invoke(removed
                ? $"[hero-bag] SM_HERODROPITEM_SUCCESS name='{name}' makeIndex={makeIndex} removed='{item.NameString}'"
                : $"[hero-bag] SM_HERODROPITEM_SUCCESS name='{name}' makeIndex={makeIndex} (not found in bag)");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HERODROPITEM_FAIL, packet =>
        {
            onDropResponse?.Invoke();
            string name = EdCode.DecodeString(packet.BodyEncoded);
            log?.Invoke($"[hero-bag] SM_HERODROPITEM_FAIL name='{name}' makeIndex={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_ADDITEMTOHEROBAG, packet =>
        {
            HeroBagExchangePending? pending = getHeroBagExchangePending?.Invoke();
            if (pending == null || !pending.Value.Pending || pending.Value.Item.MakeIndex == 0)
            {
                log?.Invoke("[hero-bag] SM_ADDITEMTOHEROBAG");
                return ValueTask.CompletedTask;
            }

            ClientItem item = pending.Value.Item;
            clearHeroBagExchangePending?.Invoke();

            if (!world.HeroBagItems.ContainsKey(item.MakeIndex))
                _ = world.TryApplyHeroAddBagItem(EncodeClientItem(item), out _);

            log?.Invoke($"[hero-bag] SM_ADDITEMTOHEROBAG name='{item.NameString}' makeIndex={item.MakeIndex}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_ADDITEMTOHEROBAG1, packet =>
        {
            HeroBagExchangePending? pending = getHeroBagExchangePending?.Invoke();
            if (pending == null || !pending.Value.Pending || pending.Value.Item.MakeIndex == 0)
            {
                log?.Invoke("[hero-bag] SM_ADDITEMTOHEROBAG1");
                return ValueTask.CompletedTask;
            }

            ClientItem item = pending.Value.Item;
            clearHeroBagExchangePending?.Invoke();

            if (!world.HeroBagItems.ContainsKey(item.MakeIndex))
                _ = world.TryApplyHeroAddBagItem(EncodeClientItem(item), out _);

            log?.Invoke($"[hero-bag] SM_ADDITEMTOHEROBAG1 name='{item.NameString}' makeIndex={item.MakeIndex}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GETITEMFROMHEROBAG, packet =>
        {
            HeroBagExchangePending? pending = getHeroBagExchangePending?.Invoke();
            if (pending == null || !pending.Value.Pending || pending.Value.Item.MakeIndex == 0)
            {
                log?.Invoke("[hero-bag] SM_GETITEMFROMHEROBAG");
                return ValueTask.CompletedTask;
            }

            ClientItem item = pending.Value.Item;
            clearHeroBagExchangePending?.Invoke();

            if (!world.BagItems.ContainsKey(item.MakeIndex))
                _ = world.TryApplyAddBagItem(EncodeClientItem(item), out _);

            log?.Invoke($"[hero-bag] SM_GETITEMFROMHEROBAG name='{item.NameString}' makeIndex={item.MakeIndex}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GETITEMFROMHEROBAG1, packet =>
        {
            HeroBagExchangePending? pending = getHeroBagExchangePending?.Invoke();
            if (pending == null || !pending.Value.Pending || pending.Value.Item.MakeIndex == 0)
            {
                log?.Invoke("[hero-bag] SM_GETITEMFROMHEROBAG1");
                return ValueTask.CompletedTask;
            }

            ClientItem item = pending.Value.Item;
            clearHeroBagExchangePending?.Invoke();

            if (!world.BagItems.ContainsKey(item.MakeIndex))
                _ = world.TryApplyAddBagItem(EncodeClientItem(item), out _);

            log?.Invoke($"[hero-bag] SM_GETITEMFROMHEROBAG1 name='{item.NameString}' makeIndex={item.MakeIndex}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROEXCHGBAGITEM_FAIL, packet =>
        {
            int code = packet.Header.Recog;

            HeroBagExchangePending? pending = getHeroBagExchangePending?.Invoke();
            if (pending == null || !pending.Value.Pending || pending.Value.Item.MakeIndex == 0)
            {
                log?.Invoke($"[hero-bag] SM_HEROEXCHGBAGITEM_FAIL code={code}");
                return ValueTask.CompletedTask;
            }

            ClientItem item = pending.Value.Item;
            bool heroToPlayer = pending.Value.HeroToPlayer;
            clearHeroBagExchangePending?.Invoke();

            string reason = code switch
            {
                0 => "Hero bag full",
                1 => "Bag full",
                2 => "Cannot move item",
                _ => $"code={code}"
            };

            bool restoreToHero = code switch
            {
                0 => false,
                1 => true,
                2 => false,
                _ => heroToPlayer
            };

            if (restoreToHero)
            {
                if (!world.HeroBagItems.ContainsKey(item.MakeIndex))
                    _ = world.TryApplyHeroAddBagItem(EncodeClientItem(item), out _);

                log?.Invoke($"[hero-bag] SM_HEROEXCHGBAGITEM_FAIL {reason} restored=hero name='{item.NameString}' makeIndex={item.MakeIndex}");
                return ValueTask.CompletedTask;
            }

            if (!world.BagItems.ContainsKey(item.MakeIndex))
                _ = world.TryApplyAddBagItem(EncodeClientItem(item), out _);

            log?.Invoke($"[hero-bag] SM_HEROEXCHGBAGITEM_FAIL {reason} restored=bag name='{item.NameString}' makeIndex={item.MakeIndex}");
            return ValueTask.CompletedTask;
        });
    }

    public readonly record struct HeroBagExchangePending(bool Pending, bool HeroToPlayer, ClientItem Item);

    private static string EncodeClientItem(ClientItem item)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref item, 1));
        return EdCode.EncodeBuffer(bytes);
    }
}
