using MirClient.Core.Util;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;

namespace MirClient.Core.Messages;

public static class MirTreasureMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string, MirColor4>? addChatLine = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_SecretProperty, packet =>
        {
            if (packet.Header.Series > 0)
                world.ApplySecretProperty(packet.Header.Param, packet.Header.Tag);

            int hintCode = packet.Header.Recog switch
            {
                0 => 7,
                2 => 10,
                -1 => 2,
                -2 => 4,
                -3 => 5,
                -4 => 3,
                -5 => 6,
                -6 => 1,
                -10 => 12,
                -11 => 13,
                -12 => 14,
                -13 => 15,
                -14 => 11,
                _ => -1
            };

            if (hintCode > 0 && MirWorldState.DescribeSecretPropertyHintCode(hintCode) is { } hint)
                addChatLine?.Invoke(hint, new MirColor4(0.92f, 0.92f, 0.92f, 1f));

            log?.Invoke($"[sp] SM_SecretProperty recog={packet.Header.Recog} luck={world.MyLuck} energy={world.MyEnergy} series={packet.Header.Series}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_ExchangeItem, packet =>
        {
            int recog = packet.Header.Recog;
            string detail = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;

            string text = recog switch
            {
                0 => string.IsNullOrWhiteSpace(detail) ? "[兑换] 成功" : $"[兑换] 成功：{detail}",
                -1 => "[兑换] 失败",
                -2 => string.IsNullOrWhiteSpace(detail) ? "[兑换] 失败" : $"[兑换] 失败：{detail}",
                -3 => "[兑换] 失败",
                -4 => "[兑换] 失败",
                _ => string.IsNullOrWhiteSpace(detail) ? $"[兑换] recog={recog}" : $"[兑换] recog={recog}：{detail}"
            };

            addChatLine?.Invoke(text, recog == 0 ? new MirColor4(0.55f, 0.95f, 0.55f, 1f) : new MirColor4(1.0f, 0.3f, 0.3f, 1f));
            log?.Invoke($"[ti] SM_ExchangeItem recog={recog} body='{detail}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_TreasureIdentify, packet =>
        {
            int recog = packet.Header.Recog;
            ushort kind = packet.Header.Param;
            string detail = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;

            bool ok = recog is 0 or 1;
            string text = ok
                ? (string.IsNullOrWhiteSpace(detail) ? $"[鉴定] 成功 type={kind}" : $"[鉴定] 成功 type={kind}：{detail}")
                : (string.IsNullOrWhiteSpace(detail) ? $"[鉴定] 失败 code={recog} type={kind}" : $"[鉴定] 失败 code={recog} type={kind}：{detail}");

            addChatLine?.Invoke(text, ok ? new MirColor4(0.55f, 0.95f, 0.55f, 1f) : new MirColor4(1.0f, 0.3f, 0.3f, 1f));
            log?.Invoke($"[ti] SM_TreasureIdentify recog={recog} param={kind} body='{detail}'");
            return ValueTask.CompletedTask;
        });
    }
}

