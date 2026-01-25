using MirClient.Core.Util;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirEquipmentMessageHandlers
{
    public readonly record struct EatPending(bool Pending, bool Hero, ClientItem Item, int SlotIndex);
    public readonly record struct UseItemPending(bool Pending, bool Hero, bool TakeOff, int Where, ClientItem Item);

    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Func<EatPending?>? getEatPending = null,
        Action? clearEatPending = null,
        Action<ushort?>? restoreEatPendingToBag = null,
        Action<string, MirColor4>? addChatLine = null,
        Func<UseItemPending?>? getUseItemPending = null,
        Action? clearUseItemPending = null,
        Action? onRefineOpen = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_SENDUSEITEMS, packet =>
        {
            MirBagItemsUpdate update = world.ApplyUseItems(packet.BodyEncoded, hero: false);
            log?.Invoke(!string.IsNullOrWhiteSpace(update.SampleNames)
                ? $"[equip] SM_SENDUSEITEMS count={update.Count} sample={update.SampleNames}"
                : $"[equip] SM_SENDUSEITEMS count={update.Count}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROUSEITEMS, packet =>
        {
            MirBagItemsUpdate update = world.ApplyUseItems(packet.BodyEncoded, hero: true);
            log?.Invoke(!string.IsNullOrWhiteSpace(update.SampleNames)
                ? $"[hero-equip] SM_HEROUSEITEMS count={update.Count} sample={update.SampleNames}"
                : $"[hero-equip] SM_HEROUSEITEMS count={update.Count}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_LAMPCHANGEDURA, packet =>
        {
            if (world.TryApplyUseItemDuraOnlyChange(Grobal2.U_RIGHTHAND, packet.Header.Recog, hero: false, out ClientItem item))
                log?.Invoke($"[equip] SM_LAMPCHANGEDURA dura={item.Dura} name='{item.NameString}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROLAMPCHANGEDURA, packet =>
        {
            if (world.TryApplyUseItemDuraOnlyChange(Grobal2.U_RIGHTHAND, packet.Header.Recog, hero: true, out ClientItem item))
                log?.Invoke($"[hero-equip] SM_HEROLAMPCHANGEDURA dura={item.Dura} name='{item.NameString}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DURACHANGE, packet =>
        {
            int index = packet.Header.Param;
            int newDuraMax = packet.Header.Tag | (packet.Header.Series << 16);

            if (world.TryApplyUseItemDuraChange(index, packet.Header.Recog, newDuraMax, hero: false, out ClientItem item))
                log?.Invoke($"[equip] SM_DURACHANGE idx={index} dura={item.Dura}/{item.DuraMax} name='{item.NameString}'");

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HERODURACHANGE, packet =>
        {
            int index = packet.Header.Param;
            int newDuraMax = packet.Header.Tag | (packet.Header.Series << 16);

            if (world.TryApplyUseItemDuraChange(index, packet.Header.Recog, newDuraMax, hero: true, out ClientItem item))
                log?.Invoke($"[hero-equip] SM_HERODURACHANGE idx={index} dura={item.Dura}/{item.DuraMax} name='{item.NameString}'");

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_TAKEON_OK, packet =>
        {
            UseItemPending? pending = getUseItemPending?.Invoke();
            if (pending is { } p && !p.Hero && !p.TakeOff)
                world.SetUseItemSlot(p.Where, hero: false, p.Item);

            clearUseItemPending?.Invoke();
            world.TryApplyMyselfFeatureChanged(packet.Header.Recog, (ushort)packet.Header.Param);
            log?.Invoke(pending is { } applied
                ? $"[equip] SM_TAKEON_OK makeIndex={applied.Item.MakeIndex} where={applied.Where} feature={packet.Header.Recog} featureEx={packet.Header.Param}"
                : $"[equip] SM_TAKEON_OK feature={packet.Header.Recog} featureEx={packet.Header.Param}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_TAKEOFF_OK, packet =>
        {
            clearUseItemPending?.Invoke();
            world.TryApplyMyselfFeatureChanged(packet.Header.Recog, (ushort)packet.Header.Param);
            log?.Invoke($"[equip] SM_TAKEOFF_OK feature={packet.Header.Recog} featureEx={packet.Header.Param}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_TAKEON_FAIL, packet =>
        {
            UseItemPending? pending = getUseItemPending?.Invoke();
            if (pending is { } p && !p.Hero && !p.TakeOff)
                world.RestoreBagItem(p.Item);

            clearUseItemPending?.Invoke();
            string reason = GetTakeOnFailReason(packet.Header.Recog);
            log?.Invoke($"[equip] SM_TAKEON_FAIL {reason}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_TAKEOFF_FAIL, packet =>
        {
            UseItemPending? pending = getUseItemPending?.Invoke();
            if (pending is { } p && !p.Hero && p.TakeOff)
                world.SetUseItemSlot(p.Where, hero: false, p.Item);

            clearUseItemPending?.Invoke();
            string reason = GetTakeOffFailReason(packet.Header.Recog);
            log?.Invoke($"[equip] SM_TAKEOFF_FAIL {reason}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROTAKEON_OK, packet =>
        {
            UseItemPending? pending = getUseItemPending?.Invoke();
            if (pending is { } p && p.Hero && !p.TakeOff)
                world.SetUseItemSlot(p.Where, hero: true, p.Item);

            clearUseItemPending?.Invoke();
            log?.Invoke(pending is { } applied
                ? $"[hero-equip] SM_HEROTAKEON_OK makeIndex={applied.Item.MakeIndex} where={applied.Where}"
                : "[hero-equip] SM_HEROTAKEON_OK");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROTAKEON_FAIL, packet =>
        {
            UseItemPending? pending = getUseItemPending?.Invoke();
            if (pending is { } p && p.Hero && !p.TakeOff)
                world.RestoreHeroBagItem(p.Item);

            clearUseItemPending?.Invoke();
            log?.Invoke($"[hero-equip] SM_HEROTAKEON_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROTAKEOFF_OK, packet =>
        {
            UseItemPending? pending = getUseItemPending?.Invoke();
            if (pending is { } p && p.Hero && p.TakeOff)
                world.RestoreHeroBagItem(p.Item);

            clearUseItemPending?.Invoke();
            log?.Invoke(pending is { } applied
                ? $"[hero-equip] SM_HEROTAKEOFF_OK makeIndex={applied.Item.MakeIndex} where={applied.Where}"
                : "[hero-equip] SM_HEROTAKEOFF_OK");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROTAKEOFF_FAIL, packet =>
        {
            UseItemPending? pending = getUseItemPending?.Invoke();
            if (pending is { } p && p.Hero && p.TakeOff)
                world.SetUseItemSlot(p.Where, hero: true, p.Item);

            clearUseItemPending?.Invoke();
            string reason = GetTakeOffFailReason(packet.Header.Recog);
            log?.Invoke($"[hero-equip] SM_HEROTAKEOFF_FAIL {reason}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_EAT_OK, packet =>
        {
            EatPending? pending = getEatPending?.Invoke();

            clearEatPending?.Invoke();

            if (pending is { } p)
                log?.Invoke($"[eat] ok makeIndex={p.Item.MakeIndex} hero={p.Hero} slot={p.SlotIndex}");
            else
                log?.Invoke($"[eat] ok recog={packet.Header.Recog} tag={packet.Header.Tag} series={packet.Header.Series}");

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_EAT_FAIL, packet =>
        {
            EatPending? pending = getEatPending?.Invoke();
            ushort? updatedDura = packet.Header.Tag > 0 ? unchecked((ushort)packet.Header.Tag) : null;

            if (pending is { } p)
            {
                restoreEatPendingToBag?.Invoke(updatedDura);
                log?.Invoke($"[eat] fail restored makeIndex={p.Item.MakeIndex} hero={p.Hero} slot={p.SlotIndex} tag={packet.Header.Tag} series={packet.Header.Series}");
            }
            else
            {
                clearEatPending?.Invoke();
                log?.Invoke($"[eat] fail (no pending) recog={packet.Header.Recog} tag={packet.Header.Tag} series={packet.Header.Series}");
            }

            if (packet.Header.Series != 0)
            {
                string message = packet.Header.Series switch
                {
                    1 => "[失败] 你的金币不足，不能释放积灵珠！",
                    2 => "[失败] 你的元宝不足，不能释放积灵珠！",
                    3 => "[失败] 你的金刚石不足，不能释放积灵珠！",
                    4 => "[失败] 你的灵符不足，不能释放积灵珠！",
                    _ => string.Empty
                };

                if (!string.IsNullOrWhiteSpace(message))
                    addChatLine?.Invoke(message, new MirColor4(1.0f, 0.3f, 0.3f, 1));
            }

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROEAT_OK, packet =>
        {
            EatPending? pending = getEatPending?.Invoke();

            clearEatPending?.Invoke();

            if (pending is { } p)
                log?.Invoke($"[hero-eat] ok makeIndex={p.Item.MakeIndex} hero={p.Hero} slot={p.SlotIndex}");
            else
                log?.Invoke($"[hero-eat] ok recog={packet.Header.Recog} tag={packet.Header.Tag} series={packet.Header.Series}");

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_HEROEAT_FAIL, packet =>
        {
            EatPending? pending = getEatPending?.Invoke();
            ushort? updatedDura = packet.Header.Tag > 0 ? unchecked((ushort)packet.Header.Tag) : null;

            if (pending is { } p)
            {
                restoreEatPendingToBag?.Invoke(updatedDura);
                log?.Invoke($"[hero-eat] fail restored makeIndex={p.Item.MakeIndex} hero={p.Hero} slot={p.SlotIndex} tag={packet.Header.Tag} series={packet.Header.Series}");
            }
            else
            {
                clearEatPending?.Invoke();
                log?.Invoke($"[hero-eat] fail (no pending) recog={packet.Header.Recog} tag={packet.Header.Tag} series={packet.Header.Series}");
            }

            if (packet.Header.Series != 0)
            {
                string message = packet.Header.Series switch
                {
                    1 => "[失败] 你的金币不足，英雄不能释放积灵珠！",
                    2 => "[失败] 你的元宝不足，英雄不能释放积灵珠！",
                    3 => "[失败] 你的金刚石不足，英雄不能释放积灵珠！",
                    4 => "[失败] 你的灵符不足，英雄不能释放积灵珠！",
                    _ => string.Empty
                };

                if (!string.IsNullOrWhiteSpace(message))
                    addChatLine?.Invoke(message, new MirColor4(1.0f, 0.3f, 0.3f, 1));
            }

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_QUERYREFINEITEM, _ =>
        {
            world.OpenRefine();
            onRefineOpen?.Invoke();
            log?.Invoke("[refine] SM_QUERYREFINEITEM");
            return ValueTask.CompletedTask;
        });
    }

    private static string GetTakeOnFailReason(int code) => code switch
    {
        -1 => "Invalid request",
        -4 => "Cannot take off existing item",
        _ => $"code={code}"
    };

    private static string GetTakeOffFailReason(int code) => code switch
    {
        -1 => "Invalid request",
        -2 => "Cannot take off",
        -3 => "Bag full",
        _ => $"code={code}"
    };
}
