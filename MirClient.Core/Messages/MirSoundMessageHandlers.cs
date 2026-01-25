using MirClient.Protocol;
using MirClient.Protocol.Codec;

namespace MirClient.Core.Messages;

public static class MirSoundMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        Action? silenceSound = null,
        Action<string, bool>? playSoundFile = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        dispatcher.Register(Grobal2.SM_PLAYSOUND, packet =>
        {
            if (packet.BodyEncoded.Length == 0)
            {
                silenceSound?.Invoke();
                return ValueTask.CompletedTask;
            }

            string file = EdCode.DecodeString(packet.BodyEncoded).Trim();
            bool loop = packet.Header.Param != 0;

            if (!string.IsNullOrWhiteSpace(file))
                playSoundFile?.Invoke(file, loop);

            log?.Invoke($"[snd] SM_PLAYSOUND loop={loop} file='{file}'");
            return ValueTask.CompletedTask;
        });
    }
}

