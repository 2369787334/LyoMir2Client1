using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;

namespace MirClient.Core.Messages;

public static class MirMissionMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<byte>? showMissionDialog = null,
        Action<int, string>? showItemDialog = null,
        Action<int, ushort>? onItemDialogSelect = null,
        Action<string>? openUrl = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_SETMISSION, packet =>
        {
            byte missionClass = packet.Header.Param > byte.MaxValue ? byte.MaxValue : (byte)packet.Header.Param;
            ushort op = packet.Header.Series;
            int missionId = packet.Header.Recog;
            ushort showFlag = packet.Header.Tag;

            if (world.TryApplySetMission(missionClass, op, missionId, showFlag, packet.BodyEncoded, out string? summary))
            {
                log?.Invoke($"[mission] SM_SETMISSION class={missionClass} op={op} id={missionId} {summary}");
                if (op == 1 && showFlag != 0)
                    showMissionDialog?.Invoke(missionClass);
            }
            else
            {
                log?.Invoke($"[mission] SM_SETMISSION decode failed class={missionClass} op={op} id={missionId} bodyLen={packet.BodyEncoded.Length}");
            }

            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_QUERYITEMDLG, packet =>
        {
            int merchantId = packet.Header.Recog;
            string prompt = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;
            log?.Invoke($"[shop] SM_QUERYITEMDLG merchant={merchantId} prompt='{prompt}'");
            showItemDialog?.Invoke(merchantId, prompt);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_ITEMDLGSELECT, packet =>
        {
            log?.Invoke($"[shop] SM_ITEMDLGSELECT recog={packet.Header.Recog} param={packet.Header.Param} tag={packet.Header.Tag} series={packet.Header.Series}");
            onItemDialogSelect?.Invoke(packet.Header.Recog, packet.Header.Param);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SETTARGETXY, packet =>
        {
            log?.Invoke($"[target] SM_SETTARGETXY recog={packet.Header.Recog} param={packet.Header.Param} tag={packet.Header.Tag} series={packet.Header.Series}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SHELLEXECUTE, packet =>
        {
            string decoded = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded).Trim() : string.Empty;
            if (string.IsNullOrWhiteSpace(decoded))
            {
                log?.Invoke("[shell] SM_SHELLEXECUTE empty");
                return ValueTask.CompletedTask;
            }

            string url = decoded;
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                url = "http://" + url;

            if (openUrl == null)
            {
                log?.Invoke($"[shell] SM_SHELLEXECUTE ignored url='{url}'");
                return ValueTask.CompletedTask;
            }

            try
            {
                openUrl(url);
                log?.Invoke($"[shell] SM_SHELLEXECUTE opened '{url}'");
            }
            catch (Exception ex)
            {
                log?.Invoke($"[shell] SM_SHELLEXECUTE failed: {ex.GetType().Name}: {ex.Message} url='{url}'");
            }

            return ValueTask.CompletedTask;
        });
    }
}
