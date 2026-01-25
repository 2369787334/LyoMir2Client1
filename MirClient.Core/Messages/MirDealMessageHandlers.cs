using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirDealMessageHandlers
{
    public enum DealPendingAction
    {
        None = 0,
        AddItem = 1,
        DelItem = 2,
    }

    public readonly record struct DealItemPending(DealPendingAction Action, ClientItem Item);

    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Func<DealItemPending?>? getPending = null,
        Action? clearPending = null,
        Action? onDealOpened = null,
        Action? onDealClosed = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_DEALTRY_FAIL, _ =>
        {
            log?.Invoke("[deal] SM_DEALTRY_FAIL");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DEALMENU, packet =>
        {
            string dealWho = EdCode.DecodeString(packet.BodyEncoded);
            world.OpenDeal(dealWho);
            clearPending?.Invoke();
            onDealOpened?.Invoke();
            log?.Invoke($"[deal] SM_DEALMENU who='{dealWho}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DEALCANCEL, _ =>
        {
            clearPending?.Invoke();
            world.CancelDealAndRestoreGold();
            onDealClosed?.Invoke();
            log?.Invoke("[deal] SM_DEALCANCEL");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DEALADDITEM_OK, packet =>
        {
            DealItemPending? pending = getPending?.Invoke();
            if (pending is { Action: DealPendingAction.AddItem } p)
            {
                clearPending?.Invoke();

                bool applied = world.TryApplyDealMyAddItem(p.Item);
                log?.Invoke(applied
                    ? $"[deal] SM_DEALADDITEM_OK name='{p.Item.NameString}' makeIndex={p.Item.MakeIndex}"
                    : $"[deal] SM_DEALADDITEM_OK apply failed name='{p.Item.NameString}' makeIndex={p.Item.MakeIndex}");
                return ValueTask.CompletedTask;
            }

            clearPending?.Invoke();
            log?.Invoke("[deal] SM_DEALADDITEM_OK");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DEALADDITEM_FAIL, packet =>
        {
            clearPending?.Invoke();
            log?.Invoke($"[deal] SM_DEALADDITEM_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DEALDELITEM_OK, _ =>
        {
            DealItemPending? pending = getPending?.Invoke();
            if (pending is { Action: DealPendingAction.DelItem } p)
            {
                clearPending?.Invoke();

                if (world.TryApplyDealMyDelItem(p.Item.MakeIndex, out ClientItem item))
                    log?.Invoke($"[deal] SM_DEALDELITEM_OK name='{item.NameString}' makeIndex={item.MakeIndex}");
                else
                    log?.Invoke($"[deal] SM_DEALDELITEM_OK apply failed makeIndex={p.Item.MakeIndex}");

                return ValueTask.CompletedTask;
            }

            clearPending?.Invoke();
            log?.Invoke("[deal] SM_DEALDELITEM_OK");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DEALDELITEM_FAIL, _ =>
        {
            clearPending?.Invoke();
            log?.Invoke("[deal] SM_DEALDELITEM_FAIL");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DEALREMOTEADDITEM, packet =>
        {
            if (world.TryApplyDealRemoteAddItem(packet.BodyEncoded, out ClientItem item))
                log?.Invoke($"[deal] SM_DEALREMOTEADDITEM name='{item.NameString}' makeIndex={item.MakeIndex}");
            else
                log?.Invoke($"[deal] SM_DEALREMOTEADDITEM decode failed (len={packet.BodyEncoded.Length}).");

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DEALREMOTEDELITEM, packet =>
        {
            if (world.TryApplyDealRemoteDelItem(packet.BodyEncoded, out ClientItem item))
                log?.Invoke($"[deal] SM_DEALREMOTEDELITEM name='{item.NameString}' makeIndex={item.MakeIndex}");
            else
                log?.Invoke($"[deal] SM_DEALREMOTEDELITEM decode failed (len={packet.BodyEncoded.Length}).");

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DEALCHGGOLD_OK, packet =>
        {
            int goldAfter = packet.Header.Param | (packet.Header.Tag << 16);
            world.ApplyDealMyGoldChanged(packet.Header.Recog, goldAfter);
            log?.Invoke($"[deal] SM_DEALCHGGOLD_OK dealGold={packet.Header.Recog} gold={goldAfter}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DEALCHGGOLD_FAIL, packet =>
        {
            int goldAfter = packet.Header.Param | (packet.Header.Tag << 16);
            world.ApplyDealMyGoldChanged(packet.Header.Recog, goldAfter);
            log?.Invoke($"[deal] SM_DEALCHGGOLD_FAIL dealGold={packet.Header.Recog} gold={goldAfter}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DEALREMOTECHGGOLD, packet =>
        {
            world.ApplyDealRemoteGold(packet.Header.Recog);
            log?.Invoke($"[deal] SM_DEALREMOTECHGGOLD gold={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DEALSUCCESS, _ =>
        {
            clearPending?.Invoke();
            world.CloseDeal();
            onDealClosed?.Invoke();
            log?.Invoke("[deal] SM_DEALSUCCESS");
            return ValueTask.CompletedTask;
        });
    }
}
