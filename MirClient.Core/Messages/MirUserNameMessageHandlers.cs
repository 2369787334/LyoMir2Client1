using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;

namespace MirClient.Core.Messages;

public static class MirUserNameMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Func<string, float?>? measureHalfNameWidth = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_USERNAME, packet =>
        {
            int recogId = packet.Header.Recog;
            byte nameColor = (byte)(packet.Header.Param & 0xFF);
            byte attribute = (byte)(packet.Header.Tag & 0xFF);
            string raw = EdCode.DecodeString(packet.BodyEncoded);

            string userName = raw;
            string descUserName = string.Empty;
            int backslash = raw.IndexOf('\\');
            if (backslash >= 0)
            {
                userName = raw[..backslash];
                descUserName = backslash + 1 < raw.Length ? raw[(backslash + 1)..] : string.Empty;
            }

            float? nameOffset = null;
            if (measureHalfNameWidth != null && !string.IsNullOrEmpty(userName))
                nameOffset = measureHalfNameWidth(userName);

            bool ok = world.TryApplyActorUserName(recogId, userName, descUserName, nameColor, attribute, nameOffset);
            if (!ok)
                log?.Invoke($"[actor] SM_USERNAME ignored recog={recogId} name='{userName}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CHANGENAMECOLOR, packet =>
        {
            int recogId = packet.Header.Recog;
            byte nameColor = (byte)(packet.Header.Param & 0xFF);
            world.TryApplyActorNameColor(recogId, nameColor);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CHANGELIGHT, packet =>
        {
            int recogId = packet.Header.Recog;
            int light = packet.Header.Param;
            world.TryApplyActorLight(recogId, light);
            return ValueTask.CompletedTask;
        });
    }
}
