using MirClient.Core.Util;
using MirClient.Protocol;

namespace MirClient.Core.Messages;

public static class MirBindItemMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        Action<string, MirColor4>? addChatLine = null,
        Action<int, bool>? showBindDialog = null,
        Action<int>? onBindResult = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        dispatcher.Register(Grobal2.SM_PICKUP_FAIL, packet =>
        {
            if (packet.Header.Recog == -1)
                addChatLine?.Invoke("物品已绑定于其他帐号，你无法捡取", new MirColor4(1.0f, 0.3f, 0.3f, 1f));

            log?.Invoke($"[item] SM_PICKUP_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_QUERYBINDITEM, packet =>
        {
            int merchantId = packet.Header.Recog;
            bool unbind = packet.Header.Param != 0;
            log?.Invoke(unbind
                ? $"[bind] SM_QUERYBINDITEM (unbind) merchant={merchantId}"
                : $"[bind] SM_QUERYBINDITEM (bind) merchant={merchantId}");
            showBindDialog?.Invoke(merchantId, unbind);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_QUERYBINDITEM_FALI, packet =>
        {
            int code = packet.Header.Recog;
            string text = code switch
            {
                1 => "[成功] 物品已经绑定到你的帐号！",
                0 => "[成功] 物品解除绑定成功！",
                -1 => "[失败] 物品未被绑定，不能解绑！",
                -2 => "[失败] 物品已绑定于其他帐号，你不能解绑！",
                -3 => "[失败] 物品已绑定于你帐号，请不要重复操作！",
                -4 => "[失败] 物品已绑定于其他帐号，不能再次绑定！",
                -6 => "[失败] 此物品不能进行帐号绑定！",
                _ => code >= 0 ? "[成功] 绑定状态已更新。" : "[失败] 绑定操作失败。"
            };

            addChatLine?.Invoke(text, code >= 0 ? new MirColor4(0.55f, 0.95f, 0.55f, 1f) : new MirColor4(1.0f, 0.35f, 0.35f, 1f));
            onBindResult?.Invoke(code);
            log?.Invoke($"[bind] SM_QUERYBINDITEM_FALI code={code}");
            return ValueTask.CompletedTask;
        });
    }
}
