using System.Diagnostics;
using MirClient.Core.Util;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;
using MirClient.Protocol.Packets;

namespace MirClient.Core.Messages;

public static class MirLoginMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState? world = null,
        Func<long>? getTimestamp = null,
        Func<long>? getTickMs = null,
        Action<int, int, int, ushort, MessageBodyWL>? onLogon = null,
        Action? sendInitialQueries = null,
        Action<string, int>? startReconnect = null,
        Action? disconnect = null,
        Action<string, MirColor4>? addChatLine = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);

        getTimestamp ??= () => Stopwatch.GetTimestamp();
        getTickMs ??= () => Environment.TickCount64;

        dispatcher.Register(Grobal2.SM_LOGON, packet =>
        {
            int recog = packet.Header.Recog;
            int x = packet.Header.Param;
            int y = packet.Header.Tag;
            ushort dir = packet.Header.Series;

            if (!EdCode.TryDecodeBuffer(packet.BodyEncoded, out MessageBodyWL wl))
            {
                wl = default;
                log?.Invoke($"[logon] body decode failed (len={packet.BodyEncoded.Length}).");
            }

            world?.ApplyServerLogon(recog, x, y, dir, wl, getTimestamp(), getTickMs());
            onLogon?.Invoke(recog, x, y, dir, wl);
            sendInitialQueries?.Invoke();
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_RECONNECT, packet =>
        {
            string decoded = EdCode.DecodeString(packet.BodyEncoded);
            if (!TryParseReconnect(decoded, out string host, out int port))
            {
                log?.Invoke($"[net] SM_RECONNECT invalid: '{decoded}'");
                return ValueTask.CompletedTask;
            }

            startReconnect?.Invoke(host, port);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_OUTOFCONNECTION, packet =>
        {
            string reason = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded).Trim() : string.Empty;
            log?.Invoke(string.IsNullOrWhiteSpace(reason)
                ? "[net] SM_OUTOFCONNECTION"
                : $"[net] SM_OUTOFCONNECTION: {reason}");

            addChatLine?.Invoke(string.IsNullOrWhiteSpace(reason)
                ? "[net] Disconnected by server."
                : $"[net] Disconnected by server: {reason}", new MirColor4(1.0f, 0.35f, 0.35f, 1f));

            disconnect?.Invoke();
            return ValueTask.CompletedTask;
        });

        dispatcher.Register((ushort)Grobal2.SM_RUNGATELOGOUT, _ =>
        {
            log?.Invoke("[net] SM_RUNGATELOGOUT");
            addChatLine?.Invoke("[net] Forced offline by server.", new MirColor4(1.0f, 0.35f, 0.35f, 1f));
            disconnect?.Invoke();
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_PASSOK_SELECTSERVER, packet =>
        {
            string servers = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;
            log?.Invoke(string.IsNullOrWhiteSpace(servers)
                ? $"[login] SM_PASSOK_SELECTSERVER recog={packet.Header.Recog}"
                : $"[login] SM_PASSOK_SELECTSERVER recog={packet.Header.Recog} '{servers}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SELECTSERVER_OK, packet =>
        {
            string detail = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;
            log?.Invoke(string.IsNullOrWhiteSpace(detail)
                ? $"[login] SM_SELECTSERVER_OK recog={packet.Header.Recog}"
                : $"[login] SM_SELECTSERVER_OK '{detail}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_QUERYCHR, packet =>
        {
            string detail = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;
            log?.Invoke(string.IsNullOrWhiteSpace(detail)
                ? "[login] SM_QUERYCHR"
                : $"[login] SM_QUERYCHR '{detail}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_QUERYCHR_FAIL, packet =>
        {
            log?.Invoke($"[login] SM_QUERYCHR_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_NEWCHR_SUCCESS, packet =>
        {
            log?.Invoke($"[login] SM_NEWCHR_SUCCESS recog={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_NEWCHR_FAIL, packet =>
        {
            log?.Invoke($"[login] SM_NEWCHR_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DELCHR_SUCCESS, packet =>
        {
            log?.Invoke($"[login] SM_DELCHR_SUCCESS recog={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DELCHR_FAIL, packet =>
        {
            log?.Invoke($"[login] SM_DELCHR_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_NEWID_SUCCESS, packet =>
        {
            log?.Invoke($"[login] SM_NEWID_SUCCESS recog={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_NEWID_FAIL, packet =>
        {
            log?.Invoke($"[login] SM_NEWID_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CHGPASSWD_SUCCESS, packet =>
        {
            log?.Invoke($"[login] SM_CHGPASSWD_SUCCESS recog={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CHGPASSWD_FAIL, packet =>
        {
            log?.Invoke($"[login] SM_CHGPASSWD_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_NEEDUPDATE_ACCOUNT, packet =>
        {
            string detail = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;
            log?.Invoke(string.IsNullOrWhiteSpace(detail)
                ? $"[login] SM_NEEDUPDATE_ACCOUNT code={packet.Header.Recog}"
                : $"[login] SM_NEEDUPDATE_ACCOUNT code={packet.Header.Recog} '{detail}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GETREGINFO, packet =>
        {
            string detail = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;
            log?.Invoke(string.IsNullOrWhiteSpace(detail) ? "[login] SM_GETREGINFO" : $"[login] SM_GETREGINFO '{detail}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_UPDATEID_SUCCESS, packet =>
        {
            log?.Invoke($"[login] SM_UPDATEID_SUCCESS recog={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_UPDATEID_FAIL, packet =>
        {
            log?.Invoke($"[login] SM_UPDATEID_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_QUERYDELCHR, packet =>
        {
            string detail = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;
            log?.Invoke(string.IsNullOrWhiteSpace(detail)
                ? $"[login] SM_QUERYDELCHR code={packet.Header.Recog} series={packet.Header.Series}"
                : $"[login] SM_QUERYDELCHR code={packet.Header.Recog} series={packet.Header.Series} '{detail}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_GETBACKDELCHR, packet =>
        {
            log?.Invoke($"[login] SM_GETBACKDELCHR code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_PASSWD_FAIL, packet =>
        {
            log?.Invoke($"[login] SM_PASSWD_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_STARTPLAY, packet =>
        {
            string detail = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;
            log?.Invoke(string.IsNullOrWhiteSpace(detail) ? "[login] SM_STARTPLAY" : $"[login] SM_STARTPLAY '{detail}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_STARTFAIL, packet =>
        {
            log?.Invoke($"[login] SM_STARTFAIL code={packet.Header.Recog}");
            addChatLine?.Invoke("[login] Start failed.", new MirColor4(1.0f, 0.35f, 0.35f, 1f));
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_VERSION_FAIL, packet =>
        {
            int crc1 = packet.Header.Recog;
            int crc2 = (packet.Header.Tag << 16) | packet.Header.Param;
            log?.Invoke($"[login] SM_VERSION_FAIL (disconnect) crc1={crc1} crc2={crc2} bodyLen={packet.BodyEncoded.Length}");
            addChatLine?.Invoke("[login] Version mismatch, disconnecting.", new MirColor4(1.0f, 0.35f, 0.35f, 1f));
            disconnect?.Invoke();
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_OVERCLIENTCOUNT, packet =>
        {
            string detail = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;
            log?.Invoke(string.IsNullOrWhiteSpace(detail)
                ? "[login] SM_OVERCLIENTCOUNT (disconnect)"
                : $"[login] SM_OVERCLIENTCOUNT (disconnect) '{detail}'");
            addChatLine?.Invoke("[login] Too many clients, disconnecting.", new MirColor4(1.0f, 0.35f, 0.35f, 1f));
            disconnect?.Invoke();
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_CDVERSION_FAIL, packet =>
        {
            string detail = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;
            log?.Invoke(string.IsNullOrWhiteSpace(detail)
                ? "[login] SM_CDVERSION_FAIL (disconnect)"
                : $"[login] SM_CDVERSION_FAIL (disconnect) '{detail}'");
            addChatLine?.Invoke("[login] CD version check failed, disconnecting.", new MirColor4(1.0f, 0.35f, 0.35f, 1f));
            disconnect?.Invoke();
            return ValueTask.CompletedTask;
        });
    }

    private static bool TryParseReconnect(string decodedBody, out string host, out int port)
    {
        host = string.Empty;
        port = 0;

        if (string.IsNullOrWhiteSpace(decodedBody))
            return false;

        string[] parts = decodedBody.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return false;

        host = parts[0].Trim();
        if (host.Length == 0)
            return false;

        if (!int.TryParse(parts[1].Trim(), out port) || port is <= 0 or > 65535)
            return false;

        return true;
    }
}
