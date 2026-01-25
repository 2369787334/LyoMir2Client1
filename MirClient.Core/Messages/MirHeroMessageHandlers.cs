using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;

namespace MirClient.Core.Messages;

public static class MirHeroMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string>? onChat = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_SENDHEROS, packet =>
        {
            if (world.TryApplySendHeros(packet.BodyEncoded, out int heroCount))
            {
                log?.Invoke($"[hero] SM_SENDHEROS selected='{world.SelectedHeroName}' heroes={heroCount}");
            }
            else
            {
                log?.Invoke($"[hero] SM_SENDHEROS decode failed (len={packet.BodyEncoded.Length}).");
            }

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_QUERYCHANGEHERO_FALI, packet =>
        {
            string who = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded).Trim() : string.Empty;

            string text = packet.Header.Recog switch
            {
                0 => string.IsNullOrWhiteSpace(who) ? "[成功] 更改英雄成功" : $"[成功] 更改英雄成功，当前英雄为：{who}",
                -1 => "[失败] 你正在创建英雄中，不能改变英雄",
                -2 => "[失败] 将要变更的英雄和当前英雄相同，改变失效",
                -3 => "[失败] 你的帐号下不存在其他角色，不能设置英雄",
                -4 => "[失败] 服务器不存在当前角色，不能改变此角色为英雄",
                -5 => "[失败] 要设置其他伴随的英雄，必须将当前英雄设置下线",
                -6 => string.IsNullOrWhiteSpace(who) ? "[失败] 当前角色在线，不能设置为英雄" : $"[失败] 当前角色已[{who}]在线，不能将此角色设置为英雄",
                -7 => "[失败] 此系统功能未开放",
                _ => string.IsNullOrWhiteSpace(who) ? $"[hero] SM_QUERYCHANGEHERO_FALI code={packet.Header.Recog}" : $"[hero] SM_QUERYCHANGEHERO_FALI code={packet.Header.Recog} who={who}"
            };

            bool success = packet.Header.Recog == 0;
            onChat?.Invoke(text);
            log?.Invoke($"[hero] SM_QUERYCHANGEHERO_FALI recog={packet.Header.Recog} who='{who}'");
            return ValueTask.CompletedTask;
        });
    }
}

