using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Messages;

public static class MirMarketMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<bool>? onMarketList = null,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_MARKET_LIST, packet =>
        {
            bool first = packet.Header.Tag != 0;
            MirBagItemsUpdate update = world.ApplyMarketList(packet.Header.Recog, packet.Header.Param, first, packet.BodyEncoded);
            onMarketList?.Invoke(first);
            log?.Invoke(!string.IsNullOrWhiteSpace(update.SampleNames)
                ? $"[market] SM_MARKET_LIST userMode={packet.Header.Recog} type={packet.Header.Param} first={(first ? 1 : 0)} page={world.MarketCurrentPage}/{world.MarketMaxPage} count={update.Count} sample={update.SampleNames}"
                : $"[market] SM_MARKET_LIST userMode={packet.Header.Recog} type={packet.Header.Param} first={(first ? 1 : 0)} page={world.MarketCurrentPage}/{world.MarketMaxPage} count={update.Count}");
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_MARKET_RESULT, packet =>
        {
            string name = packet.Header.Param switch
            {
                Grobal2.UMResult_Success => "Success",
                Grobal2.UMResult_Fail => "Fail",
                Grobal2.UMResult_ReadFail => "ReadFail",
                Grobal2.UMResult_WriteFail => "WriteFail",
                Grobal2.UMResult_ReadyToSell => "ReadyToSell",
                Grobal2.UMResult_OverSellCount => "OverSellCount",
                Grobal2.UMResult_LessMoney => "LessMoney",
                Grobal2.UMResult_LessLevel => "LessLevel",
                Grobal2.UMResult_MaxBagItemCount => "MaxBagItemCount",
                Grobal2.UMResult_NoItem => "NoItem",
                Grobal2.UMResult_DontSell => "DontSell",
                Grobal2.UMResult_DontBuy => "DontBuy",
                Grobal2.UMResult_DontGetMoney => "DontGetMoney",
                Grobal2.UMResult_MarketNotReady => "MarketNotReady",
                Grobal2.UMResult_LessTrustMoney => "LessTrustMoney",
                Grobal2.UMResult_MaxTrustMoney => "MaxTrustMoney",
                Grobal2.UMResult_CancelFail => "CancelFail",
                Grobal2.UMResult_OverMoney => "OverMoney",
                Grobal2.UMResult_SellOK => "SellOK",
                Grobal2.UMResult_BuyOK => "BuyOK",
                Grobal2.UMResult_CancelOK => "CancelOK",
                Grobal2.UMResult_GetPayOK => "GetPayOK",
                _ => "Unknown"
            };

            log?.Invoke($"[market] SM_MARKET_RESULT code={packet.Header.Param}({name}) recog={packet.Header.Recog} tag={packet.Header.Tag} series={packet.Header.Series}");
            return ValueTask.CompletedTask;
        });
    }
}
