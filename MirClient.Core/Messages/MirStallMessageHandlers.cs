using MirClient.Core.Util;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;

namespace MirClient.Core.Messages;

public static class MirStallMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<int, int, string>? onUserStall = null,
        Action<int, StallActorMarker>? onOpenStall = null,
        Action<int, string?>? onUpdateStallItem = null,
        Action<int, string?>? onBuyStallItem = null,
        Action<string, MirColor4>? addChatLine = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        static MirColor4 FailureColor() => new(1.0f, 0.35f, 0.35f, 1f);

        dispatcher.Register(Grobal2.SM_USERSTALL, packet =>
        {
            int actorId = packet.Header.Recog;
            if (world.TryApplyUserStall(actorId, packet.BodyEncoded, out int itemCount, out string stallName))
            {
                onUserStall?.Invoke(actorId, itemCount, stallName);
                log?.Invoke($"[stall] SM_USERSTALL actor={actorId} items={itemCount} name='{stallName}'");
            }
            else
            {
                log?.Invoke($"[stall] SM_USERSTALL decode failed actor={actorId} bodyLen={packet.BodyEncoded.Length}");
            }

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_OPENSTALL, packet =>
        {
            int codeOrActorId = packet.Header.Recog;
            if (codeOrActorId < 0)
            {
                int code = codeOrActorId;
                string decoded = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded).Trim() : string.Empty;
                string? text = code switch
                {
                    -1 => "[失败] 当前地图不允许摆摊",
                    -2 => "[失败] 骑马状态不能摆摊",
                    -3 => "[失败] 你周围没有位置摆摊",
                    -4 => "[失败] 交易状态不允许摆摊",
                    -5 => "[失败] 物品出售价格类型定义错误",
                    -6 => "[失败] 金币价格定义超过允许的范围(1~150,000,000)",
                    -7 => "[失败] 元宝价格定义超过允许的范围(1~8,000,000)",
                    -8 => "[失败] 物品不存在",
                    -9 => string.IsNullOrWhiteSpace(decoded) ? "[失败] 不允许出售" : $"[失败] {decoded} 不允许出售",
                    -10 => "[失败] 同一物品不可多次出售",
                    -11 => string.IsNullOrWhiteSpace(decoded) ? "[失败] 已绑定于其他帐号，不允许出售" : $"[失败] {decoded} 已绑定于其他帐号，不允许出售",
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(text))
                    addChatLine?.Invoke(text, FailureColor());

                log?.Invoke(string.IsNullOrWhiteSpace(text)
                    ? (string.IsNullOrWhiteSpace(decoded)
                        ? $"[stall] SM_OPENSTALL error={code}"
                        : $"[stall] SM_OPENSTALL error={code} extra='{decoded}'")
                    : $"[stall] SM_OPENSTALL error={code} '{text}'");
                return ValueTask.CompletedTask;
            }

            ushort dir = packet.Header.Series;
            ushort x = packet.Header.Param;
            ushort y = packet.Header.Tag;

            if (world.TryApplyOpenStall(codeOrActorId, dir, x, y, packet.BodyEncoded, out StallActorMarker stall))
            {
                onOpenStall?.Invoke(codeOrActorId, stall);
                log?.Invoke($"[stall] SM_OPENSTALL actor={codeOrActorId} open={stall.Open} looks={stall.Looks} name='{stall.Name}'");
            }
            else
            {
                log?.Invoke($"[stall] SM_OPENSTALL decode failed actor={codeOrActorId} bodyLen={packet.BodyEncoded.Length}");
            }

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_BUYSTALLITEM, packet =>
        {
            int code = packet.Header.Recog;
            string decoded = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded).Trim() : string.Empty;
            string? text = code switch
            {
                -1 => "[失败] 物品已经被售出",
                -2 => string.IsNullOrWhiteSpace(decoded) ? "[失败] 对方携带的金币太多，无法装下你将交易给他(她)的元宝" : $"[失败] {decoded}携带的金币太多，无法装下你将交易给他(她)的元宝",
                -3 => string.IsNullOrWhiteSpace(decoded) ? "[失败] 你的金币不足以购买" : $"[失败] 你的金币不足以购买：{decoded}",
                -4 => string.IsNullOrWhiteSpace(decoded) ? "[失败] 对方携带的元宝太多，无法装下你将交易给他(她)的元宝" : $"[失败] {decoded}携带的元宝太多，无法装下你将交易给他(她)的元宝",
                -5 => string.IsNullOrWhiteSpace(decoded) ? "[失败] 你的元宝不足以购买" : $"[失败] 你的元宝不足以购买 {decoded}",
                -6 => "[失败] 购买的物品不存在",
                -7 => "[失败] 你无法携带更多的物品",
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(text))
                addChatLine?.Invoke(text, FailureColor());

            onBuyStallItem?.Invoke(code, string.IsNullOrWhiteSpace(text) ? decoded : text);
            log?.Invoke($"[stall] SM_BUYSTALLITEM code={code} bodyLen={packet.BodyEncoded.Length}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_UPDATESTALLITEM, packet =>
        {
            int code = packet.Header.Recog;
            string decoded = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded).Trim() : string.Empty;
            string? text = code switch
            {
                -1 => "[失败] 交易状态不能更改摆摊物品！",
                -2 => "[失败] 摊位已满，不能继续增加物品！",
                -3 => "[失败] 物品ID错误，不能增加到摊位中！",
                -4 => "[失败] 物品出售价格类型定义错误，不能增加到摊位中！",
                -5 => "[失败] 物品不存在，不能增加到摊位中！",
                -6 => "[失败] 金币价格定义超过允许的范围(1~150,000,000)",
                -7 => "[失败] 元宝价格定义超过允许的范围(1~8,000,000)",
                -8 => "[失败] 物品不存在",
                -9 => string.IsNullOrWhiteSpace(decoded) ? "[失败] 不允许出售" : $"[失败] {decoded} 不允许出售",
                -10 => "[失败] 没有可取消的物品！",
                -11 => "[失败] 不能取消此物品，物品已经出售了！",
                -12 => "[失败] 物品不存在，不能移动到包裹中！",
                -13 => string.IsNullOrWhiteSpace(decoded) ? "[失败] 已绑定于其他帐号，不允许出售" : $"[失败] {decoded} 已绑定于其他帐号，不允许出售",
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(text))
                addChatLine?.Invoke(text, FailureColor());

            onUpdateStallItem?.Invoke(code, string.IsNullOrWhiteSpace(text) ? decoded : text);
            log?.Invoke($"[stall] SM_UPDATESTALLITEM code={code} bodyLen={packet.BodyEncoded.Length}");
            return ValueTask.CompletedTask;
        });
    }
}
