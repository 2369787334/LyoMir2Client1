using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirStorageMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_SENDUSERSTORAGEITEM, packet =>
        {
            world.ApplyMerchantMode(packet.Header.Recog, MirMerchantMode.Storage);
            log?.Invoke($"[storage] SM_SENDUSERSTORAGEITEM merchant={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_STORAGE_OK, _ =>
        {
            log?.Invoke("[storage] SM_STORAGE_OK");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_STORAGE_FULL, _ =>
        {
            log?.Invoke("[storage] SM_STORAGE_FULL");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_STORAGE_FAIL, packet =>
        {
            log?.Invoke($"[storage] SM_STORAGE_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SAVEITEMLIST, packet =>
        {
            MirBagItemsUpdate update = world.ApplySaveItemList(packet.Header.Recog, packet.BodyEncoded);
            log?.Invoke(!string.IsNullOrWhiteSpace(update.SampleNames)
                ? $"[storage] SM_SAVEITEMLIST count={update.Count} sample={update.SampleNames}"
                : $"[storage] SM_SAVEITEMLIST count={update.Count}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_TAKEBACKSTORAGEITEM_OK, packet =>
        {
            bool removed = world.TryApplyTakeBackStorageItem(packet.Header.Recog, packet.Header.Param, out ClientItem item);
            log?.Invoke(removed
                ? $"[storage] SM_TAKEBACKSTORAGEITEM_OK name='{item.NameString}' makeIndex={item.MakeIndex}"
                : $"[storage] SM_TAKEBACKSTORAGEITEM_OK makeIndex={packet.Header.Recog} index={packet.Header.Param}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_TAKEBACKSTORAGEITEM_FAIL, packet =>
        {
            log?.Invoke($"[storage] SM_TAKEBACKSTORAGEITEM_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_TAKEBACKSTORAGEITEM_FULLBAG, _ =>
        {
            log?.Invoke("[storage] SM_TAKEBACKSTORAGEITEM_FULLBAG");
            return ValueTask.CompletedTask;
        });
    }
}

