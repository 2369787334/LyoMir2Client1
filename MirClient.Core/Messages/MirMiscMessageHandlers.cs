using System.Runtime.InteropServices;
using MirClient.Core.Util;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirMiscMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string, MirColor4>? addChatLine = null,
        Func<int, string, ValueTask>? onPlayDiceSelect = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_APPRCHANGED, _ => ValueTask.CompletedTask);

        dispatcher.Register(Grobal2.SM_DLGMSG, packet =>
        {
            string text = EdCode.DecodeString(packet.BodyEncoded);
            if (!string.IsNullOrWhiteSpace(text))
                addChatLine?.Invoke(text.Trim(), new MirColor4(0.92f, 0.92f, 0.92f, 1f));
            log?.Invoke($"[dlg] {text}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_MYSTATUS, packet =>
        {
            world.ApplyMyHungryState(packet.Header.Param);
            log?.Invoke($"[user] SM_MYSTATUS recog={packet.Header.Recog} param={packet.Header.Param} tag={packet.Header.Tag}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_MENU_OK, packet =>
        {
            string text = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;
            if (!string.IsNullOrWhiteSpace(text))
                addChatLine?.Invoke(text.Trim(), new MirColor4(0.92f, 0.92f, 0.92f, 1f));
            log?.Invoke(!string.IsNullOrWhiteSpace(text)
                ? $"[dlg] SM_MENU_OK {text}"
                : "[dlg] SM_MENU_OK");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_PLAYDICE, async packet =>
        {
            int wlEncodedLen = EdCode.GetEncodedLength(Marshal.SizeOf<MessageBodyWL>());
            if (packet.BodyEncoded.Length < wlEncodedLen)
            {
                log?.Invoke($"[dice] SM_PLAYDICE body too short (len={packet.BodyEncoded.Length}).");
                return;
            }

            string wlEncoded = packet.BodyEncoded[..wlEncodedLen];
            string dataEncoded = packet.BodyEncoded[wlEncodedLen..];

            if (!EdCode.TryDecodeBuffer(wlEncoded, out MessageBodyWL wl))
            {
                log?.Invoke($"[dice] SM_PLAYDICE decode failed (len={packet.BodyEncoded.Length}).");
                return;
            }

            string data = dataEncoded.Length > 0 ? EdCode.DecodeString(dataEncoded) : string.Empty;

            int lp1 = wl.Param1;
            int lp2 = wl.Param2;
            int lt1 = wl.Tag1;

            string points = string.Join(", ",
                lp1 & 0xFF, (lp1 >> 8) & 0xFF, (lp1 >> 16) & 0xFF, (lp1 >> 24) & 0xFF,
                lp2 & 0xFF, (lp2 >> 8) & 0xFF, (lp2 >> 16) & 0xFF, (lp2 >> 24) & 0xFF,
                lt1 & 0xFF, (lt1 >> 8) & 0xFF);

            addChatLine?.Invoke($"[骰子] 点数=[{points}] data='{data.Trim()}'", new MirColor4(0.95f, 0.85f, 0.25f, 1f));
            log?.Invoke($"[dice] SM_PLAYDICE count={packet.Header.Param} merchant={packet.Header.Recog} points=[{points}] data='{data.Trim()}'");

            if (onPlayDiceSelect != null)
                await onPlayDiceSelect(packet.Header.Recog, data).ConfigureAwait(false);
        });

        dispatcher.Register(Grobal2.SM_PASSWORDSTATUS, packet =>
        {
            log?.Invoke($"[pwd] SM_PASSWORDSTATUS recog={packet.Header.Recog} param={packet.Header.Param} tag={packet.Header.Tag} series={packet.Header.Series} bodyLen={packet.BodyEncoded.Length}");
            return ValueTask.CompletedTask;
        });
    }
}
