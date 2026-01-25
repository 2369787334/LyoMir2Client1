using System.Diagnostics;

namespace MirClient.Core.Messages;

public sealed class MirMessagePump
{
    private readonly IMirPacketSource _packetSource;

    public MirMessagePump(IMirPacketSource packetSource)
    {
        ArgumentNullException.ThrowIfNull(packetSource);
        _packetSource = packetSource;
    }

    public MirMessageDispatcher? Dispatcher { get; set; }
    public bool Enabled { get; set; }
    public int MaxPacketsPerPump { get; set; } = 200;
    public int BudgetMs { get; set; } = 3;

    public event Action<Exception>? HandlerError;

    public int Pump(long startTimestamp)
    {
        if (!Enabled || Dispatcher == null)
            return 0;

        int maxPackets = MaxPacketsPerPump;
        long budgetTicks = BudgetMs <= 0
            ? long.MaxValue
            : (long)(Stopwatch.Frequency * (BudgetMs / 1000.0));

        int processed = 0;
        while (processed < maxPackets && _packetSource.TryDequeuePacket(out MirServerPacket packet))
        {
            try
            {
                Dispatcher.DispatchAsync(packet).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                HandlerError?.Invoke(ex);
            }

            processed++;

            if (BudgetMs > 0 && Stopwatch.GetTimestamp() - startTimestamp >= budgetTicks)
                break;
        }

        return processed;
    }
}
