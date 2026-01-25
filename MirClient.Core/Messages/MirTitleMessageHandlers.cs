using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;

namespace MirClient.Core.Messages;

public static class MirTitleMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string, bool>? onChat = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_SENDTITLES, packet =>
        {
            int compressedLen = packet.Header.Recog;
            if (world.TryApplyServerTitles(compressedLen, packet.BodyEncoded, out int titleCount))
            {
                log?.Invoke($"[title] SM_SENDTITLES bytes={compressedLen} items={titleCount}");
            }
            else
            {
                log?.Invoke($"[title] SM_SENDTITLES decode failed (bytes={compressedLen} bodyLen={packet.BodyEncoded.Length}).");
            }

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_MYTITLES, packet =>
        {
            bool hero = packet.Header.Recog != 0;
            world.TryApplyMyTitles(hero, packet.BodyEncoded, out int titleCount);
            log?.Invoke($"[title] SM_MYTITLES hero={hero} items={titleCount}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CHANGETITLE, packet =>
        {
            string text = packet.Header.Recog switch
            {
                0 => "称号已改变",
                -1 => "[失败] 称号索引错误",
                -2 => "[失败] 称号不存在",
                _ => $"[title] SM_CHANGETITLE code={packet.Header.Recog}"
            };

            bool success = packet.Header.Recog == 0;
            onChat?.Invoke(text, success);
            log?.Invoke($"[title] SM_CHANGETITLE recog={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });
    }
}

