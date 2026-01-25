using MirClient.Core.Util;
using MirClient.Core.World;
using MirClient.Protocol;
using MirClient.Protocol.Codec;

namespace MirClient.Core.Messages;

public static class MirBoxMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string, MirColor4>? addChatLine = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_CLOSEBOX, packet =>
        {
            if (packet.Header.Recog == 0)
                addChatLine?.Invoke("至少需要预留六个空位", new MirColor4(0.92f, 0.92f, 0.92f, 1f));
            else
                world.CloseBox();

            log?.Invoke($"[box] SM_CLOSEBOX recog={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SELETEBOXFLASH, packet =>
        {
            world.ApplySelectBoxFlash(packet.Header.Recog);
            log?.Invoke($"[box] SM_SELETEBOXFLASH itemIdx={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_OPENBOX, packet =>
        {
            int recog = packet.Header.Recog;
            int param = packet.Header.Param;
            if (world.TryApplyOpenBox(param, packet.BodyEncoded, out int itemCount))
                log?.Invoke($"[box] SM_OPENBOX recog={recog} param={param} items={itemCount} nameMaxLen={world.BoxNameMaxLen}");
            else
                log?.Invoke($"[box] SM_OPENBOX decode failed (recog={recog} param={param} bodyLen={packet.BodyEncoded.Length})");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_OPENBOX_FAIL, packet =>
        {
            world.CloseBox();

            string? msg = packet.Header.Recog switch
            {
                2 => "宝箱与钥匙类型不匹配",
                3 => "至少需要预留六个空位",
                _ => null
            };

            if (!string.IsNullOrWhiteSpace(msg))
                addChatLine?.Invoke(msg, new MirColor4(1.0f, 0.35f, 0.35f, 1f));

            log?.Invoke($"[box] SM_OPENBOX_FAIL code={packet.Header.Recog}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_BOOK, packet =>
        {
            int merchantId = packet.Header.Recog;
            int path = packet.Header.Param;
            int page = packet.Header.Tag;
            string label = packet.BodyEncoded.Length > 0 ? EdCode.DecodeString(packet.BodyEncoded) : string.Empty;

            world.ApplyOpenBook(merchantId, path, page, label);
            log?.Invoke($"[book] SM_BOOK merchant={merchantId} path={path} page={page} label='{label}'");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_LEVELRANK, packet =>
        {
            int page = packet.Header.Recog;
            int type = packet.Header.Param;

            if (page < 0)
            {
                string msg = type is >= 4 and <= 7 ? "你的英雄在该版没有排名" : "你在该版没有排名";
                addChatLine?.Invoke($"[提示]: {msg}", new MirColor4(1.0f, 0.35f, 0.35f, 1f));

                world.PrepareLevelRankRequest(page, type);
                log?.Invoke($"[rank] SM_LEVELRANK no-rank page={page} type={type}");
                return ValueTask.CompletedTask;
            }

            if (world.TryApplyLevelRank(page, type, packet.BodyEncoded, out int count))
                log?.Invoke($"[rank] SM_LEVELRANK page={page} type={type} count={count}");
            else
                log?.Invoke($"[rank] SM_LEVELRANK decode failed (page={page} type={type} bodyLen={packet.BodyEncoded.Length})");

            return ValueTask.CompletedTask;
        });
    }
}
