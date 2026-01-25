using System.Runtime.InteropServices;
using MirClient.Core.Util;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirMerchantMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string, MirColor4>? addChatLine = null,
        Func<ItemSumCountPending?>? getItemSumCountPending = null,
        Action? clearItemSumCountPending = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_MERCHANTSAY, packet =>
        {
            string decoded = EdCode.DecodeString(packet.BodyEncoded);
            (string npcName, string saying) = world.ApplyMerchantSay(packet.Header.Recog, packet.Header.Param, decoded);

            string preview = saying;
            if (preview.Length > 120)
                preview = preview[..120] + "...";

            log?.Invoke($"[merchant] SM_MERCHANTSAY merchant={packet.Header.Recog} face={packet.Header.Param} npc='{npcName}' saying='{preview}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_MERCHANTDLGCLOSE, _ =>
        {
            world.CloseMerchantDialog();
            log?.Invoke("[merchant] SM_MERCHANTDLGCLOSE");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SENDGOODSLIST, packet =>
        {
            MirBagItemsUpdate update = world.ApplyMerchantGoodsList(packet.Header.Recog, packet.Header.Param, packet.BodyEncoded, MirMerchantMode.Buy);
            log?.Invoke(!string.IsNullOrWhiteSpace(update.SampleNames)
                ? $"[merchant] SM_SENDGOODSLIST merchant={packet.Header.Recog} mode={MirMerchantMode.Buy} count={update.Count} sample={update.SampleNames}"
                : $"[merchant] SM_SENDGOODSLIST merchant={packet.Header.Recog} mode={MirMerchantMode.Buy} count={update.Count}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SENDUSERMAKEDRUGITEMLIST, packet =>
        {
            MirBagItemsUpdate update = world.ApplyMerchantGoodsList(packet.Header.Recog, packet.Header.Param, packet.BodyEncoded, MirMerchantMode.MakeDrug);
            log?.Invoke(!string.IsNullOrWhiteSpace(update.SampleNames)
                ? $"[merchant] SM_SENDUSERMAKEDRUGITEMLIST merchant={packet.Header.Recog} mode={MirMerchantMode.MakeDrug} count={update.Count} sample={update.SampleNames}"
                : $"[merchant] SM_SENDUSERMAKEDRUGITEMLIST merchant={packet.Header.Recog} mode={MirMerchantMode.MakeDrug} count={update.Count}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SENDUSERSELL, packet =>
        {
            world.ApplyMerchantMode(packet.Header.Recog, MirMerchantMode.Sell);
            log?.Invoke($"[merchant] SM_SENDUSERSELL merchant={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SENDUSERREPAIR, packet =>
        {
            world.ApplyMerchantMode(packet.Header.Recog, MirMerchantMode.Repair);
            log?.Invoke($"[merchant] SM_SENDUSERREPAIR merchant={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SENDBUYPRICE, packet =>
        {
            world.ApplySellPriceQuote(packet.Header.Recog);
            log?.Invoke($"[merchant] SM_SENDBUYPRICE price={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SENDBOOKCNT, packet =>
        {
            world.ApplyBookCountQuote(packet.Header.Recog);
            log?.Invoke($"[merchant] SM_SENDBOOKCNT bookCnt={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SENDDETAILGOODSLIST, packet =>
        {
            MirBagItemsUpdate update = world.ApplyMerchantDetailGoodsList(packet.Header.Recog, packet.Header.Param, packet.Header.Tag, packet.BodyEncoded);
            log?.Invoke(!string.IsNullOrWhiteSpace(update.SampleNames)
                ? $"[merchant] SM_SENDDETAILGOODSLIST merchant={packet.Header.Recog} topLine={packet.Header.Tag} count={update.Count} sample={update.SampleNames}"
                : $"[merchant] SM_SENDDETAILGOODSLIST merchant={packet.Header.Recog} topLine={packet.Header.Tag} count={update.Count}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_USERSELLITEM_OK, packet =>
        {
            world.ApplyGoldChanged(packet.Header.Recog, world.MyGameGold);
            log?.Invoke($"[merchant] SM_USERSELLITEM_OK gold={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_USERSELLITEM_FAIL, packet =>
        {
            log?.Invoke($"[merchant] SM_USERSELLITEM_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_USEREXCHGITEM_FAIL, packet =>
        {
            log?.Invoke($"[merchant] SM_USEREXCHGITEM_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_USERSELLCOUNTITEM_OK, packet =>
        {
            world.ApplyGoldChanged(packet.Header.Recog, world.MyGameGold);
            log?.Invoke($"[merchant] SM_USERSELLCOUNTITEM_OK gold={packet.Header.Recog} param={packet.Header.Param} tag={packet.Header.Tag}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_USERSELLCOUNTITEM_FAIL, packet =>
        {
            log?.Invoke($"[merchant] SM_USERSELLCOUNTITEM_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SENDREPAIRCOST, packet =>
        {
            world.ApplyRepairCostQuote(packet.Header.Recog);
            log?.Invoke($"[merchant] SM_SENDREPAIRCOST cost={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_USERREPAIRITEM_OK, packet =>
        {
            world.ApplyGoldChanged(packet.Header.Recog, world.MyGameGold);
            log?.Invoke($"[merchant] SM_USERREPAIRITEM_OK gold={packet.Header.Recog} dura={packet.Header.Param}/{packet.Header.Tag}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_USERREPAIRITEM_FAIL, packet =>
        {
            log?.Invoke($"[merchant] SM_USERREPAIRITEM_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_BUYITEM_SUCCESS, packet =>
        {
            int soldOutId = packet.Header.Param | (packet.Header.Tag << 16);
            world.ApplyBuyItemSuccess(packet.Header.Recog, soldOutId);
            log?.Invoke($"[merchant] SM_BUYITEM_SUCCESS gold={packet.Header.Recog} soldOutId={soldOutId}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_BUYITEM_FAIL, packet =>
        {
            int code = packet.Header.Recog;
            string text = code switch
            {
                1 => "[商店] 购买失败",
                2 => "[商店] 背包/负重不足，无法购买",
                3 => "[商店] 金币不足，无法购买",
                _ => $"[商店] 购买失败 (code={code})"
            };

            addChatLine?.Invoke(text, new MirColor4(1.0f, 0.35f, 0.35f, 1f));
            log?.Invoke($"[merchant] SM_BUYITEM_FAIL code={code}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_MAKEDRUG_SUCCESS, packet =>
        {
            world.ApplyGoldChanged(packet.Header.Recog, world.MyGameGold);
            log?.Invoke($"[merchant] SM_MAKEDRUG_SUCCESS gold={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_MAKEDRUG_FAIL, packet =>
        {
            log?.Invoke($"[merchant] SM_MAKEDRUG_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_ITEMSUMCOUNT_FAIL, packet =>
        {
            static MirColor4 FailureColor() => new(1.0f, 0.35f, 0.35f, 1f);

            bool hero = packet.Header.Series != 0;
            int code = packet.Header.Recog;
            ushort remain = packet.Header.Param;

            ItemSumCountPending? pending = getItemSumCountPending?.Invoke();

            if (pending == null || !pending.Value.Pending || pending.Value.Item.MakeIndex == 0)
            {
                if (code == 0)
                {
                    string text = hero
                        ? $"(英雄)重叠失败,物品最高数量是 {Grobal2.MAX_OVERLAPITEM}"
                        : $"重叠失败,物品最高数量是 {Grobal2.MAX_OVERLAPITEM}";
                    addChatLine?.Invoke(text, FailureColor());
                }

                log?.Invoke($"[item] SM_ITEMSUMCOUNT_FAIL code={code} remain={remain} hero={hero} (no pending)");
                return ValueTask.CompletedTask;
            }

            ClientItem item = pending.Value.Item;
            clearItemSumCountPending?.Invoke();

            if (code == 0)
            {
                string text = hero
                    ? $"(英雄)重叠失败,物品最高数量是 {Grobal2.MAX_OVERLAPITEM}"
                    : $"重叠失败,物品最高数量是 {Grobal2.MAX_OVERLAPITEM}";
                addChatLine?.Invoke(text, FailureColor());
            }
            else
            {
                item.Dura = remain;
            }

            if (item.Dura > 0)
            {
                if (pending.Value.Hero)
                {
                    _ = world.TryApplyHeroAddBagItem(EncodeClientItem(item), out _);
                }
                else
                {
                    _ = world.TryApplyAddBagItem(EncodeClientItem(item), out _);
                }
            }

            log?.Invoke($"[item] SM_ITEMSUMCOUNT_FAIL code={code} remain={remain} hero={hero} restored={(item.Dura > 0 ? "yes" : "no")} makeIndex={item.MakeIndex} org={pending.Value.OrgMakeIndex} ex={pending.Value.ExMakeIndex}");
            return ValueTask.CompletedTask;
        });
    }

    public readonly record struct ItemSumCountPending(bool Pending, bool Hero, int OrgMakeIndex, int ExMakeIndex, ClientItem Item);

    private static string EncodeClientItem(ClientItem item)
    {
        ReadOnlySpan<byte> bytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref item, 1));
        return EdCode.EncodeBuffer(bytes);
    }
}
