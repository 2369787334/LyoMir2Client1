using MirClient.Core.Util;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public enum MirYbDealDialogMode
{
    Sell = 0,
    Deal = 1
}

public readonly record struct MirYbDealDialog(
    MirYbDealDialogMode Mode,
    int PostPrice,
    string CharName,
    string TargetName,
    string PostTime,
    ClientItem[] Items);

public static class MirYbDealMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        Action<string, MirColor4>? addChatLine = null,
        Action<MirYbDealDialog>? showDealDialog = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        static MirColor4 SuccessColor() => new(0.55f, 0.95f, 0.55f, 1f);
        static MirColor4 FailureColor() => new(1.0f, 0.35f, 0.35f, 1f);
        const int maxDealItems = 10;

        dispatcher.Register(Grobal2.SM_AFFIRMYBDEA_FAIL, packet =>
        {
            int code = packet.Header.Recog;
            string text = code switch
            {
                1 => "[成功]: 交易成功！",
                -1 => "[失败]：进行交易失败，请稍候操作！",
                -2 => "[失败]：不存在交易订单，交易失败！",
                -3 => "[失败]：请先进行元宝冲值！",
                -4 => "[失败]：你的背包空位不足，请整理后再进行操作",
                -5 => "[失败]：该订单已超时，你无法收购，只能[取消收购]！",
                -6 => "[失败]：您持有的元宝数不足以收购！",
                _ => "[失败]：未知错误，交易失败！"
            };

            addChatLine?.Invoke(text, code == 1 ? SuccessColor() : FailureColor());
            log?.Invoke($"[yb] SM_AFFIRMYBDEA_FAIL code={code}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CANCELYBSELL_FAIL, packet =>
        {
            int code = packet.Header.Recog;
            string text = code switch
            {
                1 => "[成功]: 取消交易成功！",
                2 => "[成功]: 取消收购成功！",
                -1 => "[失败]：取消交易失败！",
                -2 => "[失败]：不存在交易订单，取消失败！",
                -3 => "[失败]：你的背包空位不足，请整理后再进行操作！",
                -4 => "[失败]：你没有可以支付的元宝，(你的物品已超期，需要支付1个元宝)",
                _ => "[失败]：未知错误，取消交易失败！"
            };

            addChatLine?.Invoke(text, code is 1 or 2 ? SuccessColor() : FailureColor());
            log?.Invoke($"[yb] SM_CANCELYBSELL_FAIL code={code}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_QUERYYBSELL_SELL, packet =>
        {
            int postPriceOrCode = packet.Header.Recog;
            ushort count = packet.Header.Series;

            if (postPriceOrCode is -1 or -2)
            {
                addChatLine?.Invoke("[失败]: 没有查询到指定的记录！", FailureColor());
            }
            else if (showDealDialog != null)
            {
                if (TryDecodeDealDialog(
                        MirYbDealDialogMode.Sell,
                        postPriceOrCode,
                        count,
                        packet.BodyEncoded,
                        maxDealItems,
                        out MirYbDealDialog dialog,
                        out string error))
                {
                    showDealDialog(dialog);
                    log?.Invoke($"[yb] SM_QUERYYBSELL_SELL price={postPriceOrCode} count={count} items={dialog.Items.Length}");
                    return ValueTask.CompletedTask;
                }

                log?.Invoke($"[yb] SM_QUERYYBSELL_SELL decode failed: {error} price={postPriceOrCode} count={count} bodyLen={packet.BodyEncoded.Length}");
            }

            log?.Invoke($"[yb] SM_QUERYYBSELL_SELL price={postPriceOrCode} count={count} bodyLen={packet.BodyEncoded.Length}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_QUERYYBSELL_DEAL, packet =>
        {
            int postPriceOrCode = packet.Header.Recog;
            ushort count = packet.Header.Series;

            if (postPriceOrCode is -1 or -2)
            {
                addChatLine?.Invoke("[失败]: 没有查询到指定的记录！", FailureColor());
            }
            else if (showDealDialog != null)
            {
                if (TryDecodeDealDialog(
                        MirYbDealDialogMode.Deal,
                        postPriceOrCode,
                        count,
                        packet.BodyEncoded,
                        maxDealItems,
                        out MirYbDealDialog dialog,
                        out string error))
                {
                    showDealDialog(dialog);
                    log?.Invoke($"[yb] SM_QUERYYBSELL_DEAL price={postPriceOrCode} count={count} items={dialog.Items.Length}");
                    return ValueTask.CompletedTask;
                }

                log?.Invoke($"[yb] SM_QUERYYBSELL_DEAL decode failed: {error} price={postPriceOrCode} count={count} bodyLen={packet.BodyEncoded.Length}");
            }

            log?.Invoke($"[yb] SM_QUERYYBSELL_DEAL price={postPriceOrCode} count={count} bodyLen={packet.BodyEncoded.Length}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_POST_FAIL2, packet =>
        {
            int code = packet.Header.Recog;
            string? extra = null;
            if (code is -13 or -14)
                extra = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded).Trim() : string.Empty;

            string text = code switch
            {
                1 => "[成功]: 系统已经成功接受您的申请！",
                -1 => "[失败]：您或者对方角色名中含有非法字符！",
                -2 => "[失败]：至少需要一件物品！",
                -3 => "[失败]：上次寄售物品已超过时间限制！",
                -4 => "[失败]：您尚未开通元宝交易系统！",
                -5 => "[失败]：您上次寄售的物品尚未成功交易！",
                -6 => "[失败]：对方已在元宝交易中！",
                -7 => "[失败]：您包裹中没有您要出售的物品！",
                -8 => "[失败]：定单失效，NPC准备未就绪，请重试！",
                -9 => "[失败]：普通交易状态下不能进行元宝买卖！",
                -10 => "[失败]：请输入合理的元宝数量，在0~9999之间！",
                -11 => "[失败]：您没有足够的金刚石，且数量在0~9999之间！",
                -12 => "[失败]：物品数量不正确！",
                -13 => string.IsNullOrWhiteSpace(extra) ? "[失败]：物品禁止寄售！" : $"[失败]：{extra} 禁止寄售！",
                -14 => string.IsNullOrWhiteSpace(extra) ? "[失败]：物品已绑定于其他帐号，禁止寄售！" : $"[失败]：{extra} 已绑定于其他帐号，禁止寄售！",
                _ => "[失败]：未知错误"
            };

            addChatLine?.Invoke(text, code == 1 ? SuccessColor() : FailureColor());
            log?.Invoke($"[yb] SM_POST_FAIL2 code={code}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_OPENDEAL_FAIL, packet =>
        {
            int code = packet.Header.Recog;
            string text = code switch
            {
                0 => "[成功]：成功开启元宝交易系统！",
                -2 => "[失败]：请先进行元宝冲值！",
                -3 => "[失败]：您已经开启元宝交易系统！",
                -4 => "[失败]：您的元宝数量不足开启交易系统！",
                _ => "[失败]：开通元宝交易系统失败！"
            };

            addChatLine?.Invoke(text, code == 0 ? SuccessColor() : FailureColor());
            log?.Invoke($"[yb] SM_OPENDEAL_FAIL code={code}");
            return ValueTask.CompletedTask;
        });
    }

    private static bool TryDecodeDealDialog(
        MirYbDealDialogMode mode,
        int postPrice,
        ushort itemCount,
        string bodyEncoded,
        int maxItems,
        out MirYbDealDialog dialog,
        out string error)
    {
        dialog = default;
        error = string.Empty;

        if (itemCount == 0)
        {
            error = "count=0";
            return false;
        }

        if (string.IsNullOrEmpty(bodyEncoded))
        {
            error = "empty body";
            return false;
        }

        SplitOnce(bodyEncoded, '/', out string headerEncoded, out string itemsEncodedPart);
        if (string.IsNullOrWhiteSpace(headerEncoded))
        {
            error = "missing header";
            return false;
        }

        string headerDecoded;
        try
        {
            headerDecoded = EdCode.DecodeString(headerEncoded);
        }
        catch (Exception ex)
        {
            error = $"decode header failed: {ex.GetType().Name}";
            return false;
        }

        SplitOnce(headerDecoded, '/', out string charName, out string rest);
        SplitOnce(rest, '/', out string targetName, out string postTime);

        int max = Math.Clamp((int)itemCount, 1, 64);
        max = Math.Min(max, Math.Clamp(maxItems, 1, 64));

        var items = new List<ClientItem>(Math.Min(max, 12));
        if (!string.IsNullOrWhiteSpace(itemsEncodedPart))
        {
            string[] parts = itemsEncodedPart.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (string part in parts)
            {
                if (items.Count >= max)
                    break;
                if (EdCode.TryDecodeBuffer(part, out ClientItem item))
                    items.Add(item);
            }
        }

        if (items.Count == 0)
        {
            error = "no items";
            return false;
        }

        dialog = new MirYbDealDialog(mode, postPrice, charName, targetName, postTime, items.ToArray());
        return true;
    }

    private static void SplitOnce(string input, char delimiter, out string head, out string tail)
    {
        if (string.IsNullOrEmpty(input))
        {
            head = string.Empty;
            tail = string.Empty;
            return;
        }

        int idx = input.IndexOf(delimiter);
        if (idx < 0)
        {
            head = input;
            tail = string.Empty;
            return;
        }

        head = idx == 0 ? string.Empty : input[..idx];
        tail = idx + 1 >= input.Length ? string.Empty : input[(idx + 1)..];
    }
}
