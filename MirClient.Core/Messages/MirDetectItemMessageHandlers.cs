using MirClient.Core.Util;
using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Messages;

public static class MirDetectItemMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string, MirColor4>? addChatLine = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_UPDATEDETECTITEM, packet =>
        {
            ushort spiritQ = packet.Header.Param;
            ushort spirit = packet.Header.Tag;
            bool updated = world.TryApplyUpdateDetectItem(spiritQ, spirit);
            log?.Invoke(updated
                ? $"[detect] SM_UPDATEDETECTITEM spiritQ={spiritQ} spirit={spirit}"
                : $"[detect] SM_UPDATEDETECTITEM ignored (no detect item) spiritQ={spiritQ} spirit={spirit}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_DETECTITEM_FALI, packet =>
        {
            int mineId = packet.Header.Recog;
            int code = packet.Header.Series;
            int x = packet.Header.Param;
            int y = packet.Header.Tag;

            world.ApplyDetectItemMineId(mineId);

            string? text = code switch
            {
                0 => "[寻宝灵媒感应到了宝物的存在，请向“上方”寻找]",
                1 => "[寻宝灵媒感应到了宝物的存在，请向“右上方”寻找]",
                2 => "[寻宝灵媒感应到了宝物的存在，请向“右方”寻找]",
                3 => "[寻宝灵媒感应到了宝物的存在，请向“右下方”寻找]",
                4 => "[寻宝灵媒感应到了宝物的存在，请向“下方”寻找]",
                5 => "[寻宝灵媒感应到了宝物的存在，请向“左下方”寻找]",
                6 => "[寻宝灵媒感应到了宝物的存在，请向“左方”寻找]",
                7 => "[寻宝灵媒感应到了宝物的存在，请向“左上方”寻找]",
                9 => "[请将灵媒装备在探索位]",
                10 => "[灵媒的灵气值已经不足，请补充灵气后再使用]",
                11 => "[使用宝物灵媒频率不能太快]",
                12 => "[这次你的宝物灵媒没有感应到宝物的存在]",
                >= 20 and <= 22 => $"[宝物就在您周围({x}/{y})，按下Alt+鼠标左键就可以挖宝了]",
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(text))
                addChatLine?.Invoke(text, new MirColor4(0.98f, 0.92f, 0.75f, 1f));

            log?.Invoke($"[detect] SM_DETECTITEM_FALI mineId={mineId} code={code} x={x} y={y}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_MOVEDETECTITEM_FALI, packet =>
        {
            int resultCode = packet.Header.Recog;
            _ = world.TryApplyMoveDetectItemResult(resultCode, out string? text);

            if (!string.IsNullOrWhiteSpace(text))
                addChatLine?.Invoke(text, new MirColor4(1.0f, 0.3f, 0.3f, 1f));

            log?.Invoke($"[detect] SM_MOVEDETECTITEM_FALI code={resultCode}");
            return ValueTask.CompletedTask;
        });
    }
}

