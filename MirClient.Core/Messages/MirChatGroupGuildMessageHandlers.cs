using MirClient.Core.World;
using MirClient.Core.Util;
using MirClient.Protocol;
using MirClient.Protocol.Codec;

namespace MirClient.Core.Messages;

public static class MirChatGroupGuildMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string, MirColor4>? addChatLine = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_HEAR, packet =>
        {
            string text = EdCode.DecodeString(packet.BodyEncoded);
            addChatLine?.Invoke(text, new MirColor4(0.92f, 0.92f, 0.92f, 1f));
            log?.Invoke($"[chat] {packet.Header.Recog}: {text}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CRY, packet =>
        {
            string text = EdCode.DecodeString(packet.BodyEncoded);
            addChatLine?.Invoke($"[cry] {text}", new MirColor4(1.0f, 0.75f, 0.25f, 1f));
            log?.Invoke($"[cry] Ident={packet.Header.Ident} Recog={packet.Header.Recog} {text}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_WHISPER, packet =>
        {
            string text = EdCode.DecodeString(packet.BodyEncoded);
            addChatLine?.Invoke($"[whisper] {text}", new MirColor4(0.9f, 0.7f, 1.0f, 1f));
            log?.Invoke($"[whisper] Ident={packet.Header.Ident} Recog={packet.Header.Recog} {text}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GROUPMESSAGE, packet =>
        {
            string text = EdCode.DecodeString(packet.BodyEncoded);
            addChatLine?.Invoke($"[group] {text}", new MirColor4(0.55f, 0.95f, 0.55f, 1f));
            log?.Invoke($"[group] Ident={packet.Header.Ident} Recog={packet.Header.Recog} {text}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GUILDMESSAGE, packet =>
        {
            string text = EdCode.DecodeString(packet.BodyEncoded);
            addChatLine?.Invoke($"[guild] {text}", new MirColor4(0.6f, 0.88f, 1.0f, 1f));
            log?.Invoke($"[guild] Ident={packet.Header.Ident} Recog={packet.Header.Recog} {text}");
            return ValueTask.CompletedTask;
        });
    }
}
