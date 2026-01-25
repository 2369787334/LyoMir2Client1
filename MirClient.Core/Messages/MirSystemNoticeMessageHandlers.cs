using MirClient.Core.Util;
using MirClient.Protocol;
using MirClient.Protocol.Codec;

namespace MirClient.Core.Messages;

public static class MirSystemNoticeMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        Action<string, MirColor4>? addChatLine = null,
        Action<string, ushort>? addMarquee = null,
        Action<string>? addBottomRight = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        dispatcher.Register(Grobal2.SM_SYSMESSAGE2, packet =>
        {
            string text = EdCode.DecodeString(packet.BodyEncoded);
            addMarquee?.Invoke(text, packet.Header.Param);
            log?.Invoke($"[sys2] {text}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SYSMESSAGE4, packet =>
        {
            string text = EdCode.DecodeString(packet.BodyEncoded);
            addBottomRight?.Invoke(text);
            log?.Invoke($"[sys4] {text}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SYSMESSAGE, packet =>
        {
            string text = EdCode.DecodeString(packet.BodyEncoded);
            addChatLine?.Invoke($"[sys] {text}", new MirColor4(1.0f, 0.55f, 0.35f, 1f));
            log?.Invoke($"[sys] {text}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SENDNOTICE, packet =>
        {
            string text = EdCode.DecodeString(packet.BodyEncoded);
            addChatLine?.Invoke($"[notice] {text}", new MirColor4(0.98f, 0.92f, 0.75f, 1f));
            log?.Invoke($"[notice] {text}");
            return ValueTask.CompletedTask;
        });
    }
}
