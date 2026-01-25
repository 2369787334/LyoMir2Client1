using System.Runtime.InteropServices;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirActorActionMessageHandlers
{
    public delegate void ActorActionHandler(
        ushort ident,
        int recogId,
        int x,
        int y,
        ushort dir,
        CharDesc desc,
        string? userName,
        string? descUserName,
        byte? nameColor);

    public delegate void ActorSimpleActionHandler(ushort ident, int recogId, int x, int y, ushort dir);

    public static void Register(
        MirMessageDispatcher dispatcher,
        Func<bool> isMapMoving,
        ActorActionHandler onActorAction,
        ActorSimpleActionHandler onActorSimpleAction,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(isMapMoving);
        ArgumentNullException.ThrowIfNull(onActorAction);
        ArgumentNullException.ThrowIfNull(onActorSimpleAction);

        int charDescEncodedLen12 = EdCode.GetEncodedLength(Marshal.SizeOf<CharDesc>());
        int charDescEncodedLen8 = EdCode.GetEncodedLength(Marshal.SizeOf<CharDesc2>());

        ValueTask OnActorAction(ushort ident, MirServerPacket packet)
        {
            int recogId = packet.Header.Recog;
            int x = packet.Header.Param;
            int y = packet.Header.Tag;
            ushort dir = packet.Header.Series;

            CharDesc desc = default;
            int descEncodedLen = 0;
            if (EdCode.TryDecodeBuffer(packet.BodyEncoded, out CharDesc2 desc2))
            {
                desc = new CharDesc { Feature = desc2.Feature, Status = desc2.Status, StatusEx = 0 };
                descEncodedLen = charDescEncodedLen8;
            }
            else if (EdCode.TryDecodeBuffer(packet.BodyEncoded, out desc))
            {
                descEncodedLen = charDescEncodedLen12;
            }

            if (descEncodedLen == 0)
            {
                if (!isMapMoving())
                    onActorSimpleAction(ident, recogId, x, y, dir);
                return ValueTask.CompletedTask;
            }

            string? userName = null;
            string? descUserName = null;
            byte? nameColor = null;

            bool hasNameTail = ident is Grobal2.SM_TURN or Grobal2.SM_BACKSTEP or Grobal2.SM_SPACEMOVE_SHOW or Grobal2.SM_SPACEMOVE_SHOW2;
            if (hasNameTail && packet.BodyEncoded.Length > descEncodedLen)
            {
                string tailEncoded = packet.BodyEncoded[descEncodedLen..];
                string tail = EdCode.DecodeString(tailEncoded);
                if (!string.IsNullOrEmpty(tail))
                {
                    string namePart = tail;
                    string colorPart = string.Empty;
                    int slash = tail.IndexOf('/');
                    if (slash >= 0)
                    {
                        namePart = tail[..slash];
                        colorPart = slash + 1 < tail.Length ? tail[(slash + 1)..] : string.Empty;
                    }

                    if (!string.IsNullOrEmpty(namePart))
                    {
                        int backslash = namePart.IndexOf('\\');
                        if (backslash >= 0)
                        {
                            userName = namePart[..backslash];
                            descUserName = backslash + 1 < namePart.Length ? namePart[(backslash + 1)..] : string.Empty;
                        }
                        else
                        {
                            userName = namePart;
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(colorPart) && int.TryParse(colorPart, out int colorInt))
                        nameColor = (byte)colorInt;
                }
            }

            if (!isMapMoving())
                onActorAction(ident, recogId, x, y, dir, desc, userName, descUserName, nameColor);

            return ValueTask.CompletedTask;
        }

        ValueTask OnActorSimpleAction(ushort ident, MirServerPacket packet)
        {
            int recogId = packet.Header.Recog;
            int x = packet.Header.Param;
            int y = packet.Header.Tag;
            ushort dir = packet.Header.Series;

            if (!isMapMoving())
                onActorSimpleAction(ident, recogId, x, y, dir);

            return ValueTask.CompletedTask;
        }

        dispatcher.Register(Grobal2.SM_TURN, packet => OnActorAction(Grobal2.SM_TURN, packet));
        dispatcher.Register(Grobal2.SM_WALK, packet => OnActorAction(Grobal2.SM_WALK, packet));
        dispatcher.Register(Grobal2.SM_RUN, packet => OnActorAction(Grobal2.SM_RUN, packet));
        dispatcher.Register(Grobal2.SM_HORSERUN, packet => OnActorAction(Grobal2.SM_HORSERUN, packet));
        dispatcher.Register(Grobal2.SM_BACKSTEP, packet => OnActorAction(Grobal2.SM_BACKSTEP, packet));
        dispatcher.Register(Grobal2.SM_RUSH, packet => OnActorAction(Grobal2.SM_RUSH, packet));
        dispatcher.Register(Grobal2.SM_RUSHEX, packet => OnActorAction(Grobal2.SM_RUSHEX, packet));
        dispatcher.Register(Grobal2.SM_RUSHKUNG, packet => OnActorAction(Grobal2.SM_RUSHKUNG, packet));
        dispatcher.Register(Grobal2.SM_SPACEMOVE_SHOW, packet => OnActorAction(Grobal2.SM_SPACEMOVE_SHOW, packet));
        dispatcher.Register(Grobal2.SM_SPACEMOVE_SHOW2, packet => OnActorAction(Grobal2.SM_SPACEMOVE_SHOW2, packet));
        dispatcher.Register(Grobal2.SM_SITDOWN, packet => OnActorAction(Grobal2.SM_SITDOWN, packet));
        dispatcher.Register(Grobal2.SM_DEATH, packet => OnActorAction(Grobal2.SM_DEATH, packet));
        dispatcher.Register(Grobal2.SM_NOWDEATH, packet => OnActorAction(Grobal2.SM_NOWDEATH, packet));
        dispatcher.Register(Grobal2.SM_SKELETON, packet => OnActorAction(Grobal2.SM_SKELETON, packet));
        dispatcher.Register(Grobal2.SM_ALIVE, packet => OnActorAction(Grobal2.SM_ALIVE, packet));

        dispatcher.Register(Grobal2.SM_FOXSTATE, packet => OnActorAction(Grobal2.SM_TURN, packet));
        dispatcher.Register(Grobal2.SM_MOVEFAIL, packet => OnActorAction(Grobal2.SM_TURN, packet));

        dispatcher.Register(Grobal2.SM_HIT, packet => OnActorSimpleAction(Grobal2.SM_HIT, packet));
        dispatcher.Register(Grobal2.SM_HEAVYHIT, packet => OnActorSimpleAction(Grobal2.SM_HEAVYHIT, packet));
        dispatcher.Register(Grobal2.SM_POWERHIT, packet => OnActorSimpleAction(Grobal2.SM_POWERHIT, packet));
        dispatcher.Register(Grobal2.SM_LONGHIT, packet => OnActorSimpleAction(Grobal2.SM_LONGHIT, packet));
        dispatcher.Register(Grobal2.SM_SQUHIT, packet => OnActorSimpleAction(Grobal2.SM_SQUHIT, packet));
        dispatcher.Register(Grobal2.SM_CRSHIT, packet => OnActorSimpleAction(Grobal2.SM_CRSHIT, packet));
        dispatcher.Register(Grobal2.SM_TWNHIT, packet => OnActorSimpleAction(Grobal2.SM_TWNHIT, packet));
        dispatcher.Register(Grobal2.SM_WIDEHIT, packet => OnActorSimpleAction(Grobal2.SM_WIDEHIT, packet));
        dispatcher.Register(Grobal2.SM_BIGHIT, packet => OnActorSimpleAction(Grobal2.SM_BIGHIT, packet));
        dispatcher.Register(Grobal2.SM_FIREHIT, packet => OnActorSimpleAction(Grobal2.SM_FIREHIT, packet));
        dispatcher.Register(Grobal2.SM_PURSUEHIT, packet => OnActorSimpleAction(Grobal2.SM_PURSUEHIT, packet));
        dispatcher.Register(Grobal2.SM_HERO_LONGHIT, packet => OnActorSimpleAction(Grobal2.SM_HERO_LONGHIT, packet));
        dispatcher.Register(Grobal2.SM_HERO_LONGHIT2, packet => OnActorSimpleAction(Grobal2.SM_HERO_LONGHIT2, packet));
        dispatcher.Register(Grobal2.SM_SMITEHIT, packet => OnActorSimpleAction(Grobal2.SM_SMITEHIT, packet));
        dispatcher.Register(Grobal2.SM_SMITELONGHIT, packet => OnActorSimpleAction(Grobal2.SM_SMITELONGHIT, packet));
        dispatcher.Register(Grobal2.SM_SMITELONGHIT2, packet => OnActorSimpleAction(Grobal2.SM_SMITELONGHIT2, packet));
        dispatcher.Register(Grobal2.SM_SMITELONGHIT3, packet => OnActorSimpleAction(Grobal2.SM_SMITELONGHIT3, packet));
        dispatcher.Register(Grobal2.SM_SMITEWIDEHIT, packet => OnActorSimpleAction(Grobal2.SM_SMITEWIDEHIT, packet));
        dispatcher.Register(Grobal2.SM_SMITEWIDEHIT2, packet => OnActorSimpleAction(Grobal2.SM_SMITEWIDEHIT2, packet));

        dispatcher.Register(Grobal2.SM_WWJATTACK, packet => OnActorSimpleAction(Grobal2.SM_WWJATTACK, packet));
        dispatcher.Register(Grobal2.SM_WSJATTACK, packet => OnActorSimpleAction(Grobal2.SM_WSJATTACK, packet));
        dispatcher.Register(Grobal2.SM_WTJATTACK, packet => OnActorSimpleAction(Grobal2.SM_WTJATTACK, packet));
        dispatcher.Register(Grobal2.SM_DIGDOWN, packet => OnActorSimpleAction(Grobal2.SM_DIGDOWN, packet));
        dispatcher.Register(Grobal2.SM_FLYAXE, packet => OnActorSimpleAction(Grobal2.SM_FLYAXE, packet));
        dispatcher.Register(Grobal2.SM_81, packet => OnActorSimpleAction(Grobal2.SM_81, packet));
        dispatcher.Register(Grobal2.SM_82, packet => OnActorSimpleAction(Grobal2.SM_82, packet));
        dispatcher.Register(Grobal2.SM_83, packet => OnActorSimpleAction(Grobal2.SM_83, packet));
        dispatcher.Register(Grobal2.SM_LIGHTING, packet => OnActorSimpleAction(Grobal2.SM_LIGHTING, packet));
        dispatcher.Register(Grobal2.SM_LIGHTING_1, packet => OnActorSimpleAction(Grobal2.SM_LIGHTING_1, packet));
        dispatcher.Register(Grobal2.SM_LIGHTING_2, packet => OnActorSimpleAction(Grobal2.SM_LIGHTING_2, packet));
        dispatcher.Register(Grobal2.SM_LIGHTING_3, packet => OnActorSimpleAction(Grobal2.SM_LIGHTING_3, packet));

        dispatcher.Register(Grobal2.SM_DIGUP, packet =>
        {
            int recogId = packet.Header.Recog;
            int x = packet.Header.Param;
            int y = packet.Header.Tag;
            ushort dir = packet.Header.Series;

            if (!EdCode.TryDecodeBuffer(packet.BodyEncoded, out MessageBodyWL wl))
            {
                log?.Invoke($"[dig] SM_DIGUP decode failed (bodyLen={packet.BodyEncoded.Length})");
                return ValueTask.CompletedTask;
            }

            var desc = new CharDesc { Feature = wl.Param1, Status = wl.Param2, StatusEx = 0 };
            if (!isMapMoving())
                onActorAction(Grobal2.SM_DIGUP, recogId, x, y, dir, desc, null, null, null);

            return ValueTask.CompletedTask;
        });
    }
}
