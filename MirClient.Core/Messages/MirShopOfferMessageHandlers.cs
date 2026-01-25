using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Messages;

public static class MirShopOfferMessageHandlers
{
    public static void Register(
        MirMessageDispatcher dispatcher,
        MirWorldState world,
        Action<string>? log = null)
    {
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(world);

        dispatcher.Register(Grobal2.SM_OFFERITEM, packet =>
        {
            HandleShopItems("SM_OFFERITEM", packet.BodyEncoded, packet.Header.Param);
            return ValueTask.CompletedTask;
        });

        dispatcher.Register(Grobal2.SM_SPECOFFERITEM, packet =>
        {
            HandleShopItems("SM_SPECOFFERITEM", packet.BodyEncoded, packet.Header.Param);
            return ValueTask.CompletedTask;
        });

        void HandleShopItems(string identName, string bodyEncoded, int sellType)
        {
            MirBagItemsUpdate update = world.ApplyShopItems(bodyEncoded, sellType);
            string sellTypeText = sellType != 0 ? sellType.ToString() : string.Empty;

            log?.Invoke(!string.IsNullOrWhiteSpace(update.SampleNames)
                ? $"[shop] {identName} count={update.Count}{(sellTypeText.Length > 0 ? $" sellType={sellTypeText}" : string.Empty)} sample={update.SampleNames}"
                : $"[shop] {identName} count={update.Count}{(sellTypeText.Length > 0 ? $" sellType={sellTypeText}" : string.Empty)}");
        }
    }
}

