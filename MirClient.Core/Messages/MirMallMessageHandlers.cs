using MirClient.Core.Util;
using MirClient.Protocol;

namespace MirClient.Core.Messages;

public static class MirMallMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        Action<string, MirColor4>? addChatLine = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        dispatcher.Register(Grobal2.SM_BUGITEMFAIL, packet =>
        {
            string text = packet.Header.Recog switch
            {
                0 => "[失败] 非法物品名",
                -1 => "[失败] 不存在你想购买的物品",
                -2 => "[失败] 请先进行元宝冲值",
                -3 => "[失败] 你帐号中的元宝数不够",
                -4 => "[失败] 你无法携带更多的物品",
                -5 => "[失败] 购买物品不在商城中",
                -6 => "[失败] 您的购买速度过快",
                _ => "[失败] 你无法购买"
            };

            addChatLine?.Invoke(text, new MirColor4(1.0f, 0.35f, 0.35f, 1f));
            log?.Invoke($"[mall] SM_BUGITEMFAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_PRESENDITEMFAIL, packet =>
        {
            bool useGold = packet.Header.Tag != 0;
            int code = packet.Header.Recog;

            string? text = code switch
            {
                1 => "[成功] 对方已经收到你的礼物",
                0 => "[失败] 非法的物品名称",
                -1 => "[失败] 抱歉，服务器不存在你想购买赠送的物品",
                -2 when useGold => "[失败] 你没有金币",
                -3 when useGold => "[失败] 你帐的金币数不够",
                -2 => "[失败] 请先进行元宝冲值",
                -3 => "[失败] 你帐号中的元宝数不够",
                -4 => "[失败] 赠送人无法携带更多的物品",
                -5 => "[失败] 你想购买物品不在商城中",
                -6 => "[失败] 您的购买速度过快",
                -7 => "[失败] 赠送人不存在或不在线",
                -8 => "[失败] 赠送人不能是自己",
                -9 => "[失败] 服务器未开启赠送功能",
                _ => "[失败] 你无法购买"
            };

            if (!string.IsNullOrWhiteSpace(text))
            {
                addChatLine?.Invoke(
                    text,
                    code == 1
                        ? new MirColor4(0.55f, 0.95f, 0.55f, 1f)
                        : new MirColor4(1.0f, 0.35f, 0.35f, 1f));
            }

            log?.Invoke($"[mall] SM_PRESENDITEMFAIL useGold={useGold} code={code}");
            return ValueTask.CompletedTask;
        });
    }
}

