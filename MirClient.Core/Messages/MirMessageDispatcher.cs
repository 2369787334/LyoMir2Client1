namespace MirClient.Core.Messages;

public sealed class MirMessageDispatcher
{
    private readonly Dictionary<ushort, Func<MirServerPacket, ValueTask>> _handlers = new();

    public event Action<MirServerPacket>? Unhandled;

    public void Register(ushort ident, Func<MirServerPacket, ValueTask> handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[ident] = handler;
    }

    public ValueTask DispatchAsync(MirServerPacket packet)
    {
        if (_handlers.TryGetValue(packet.Header.Ident, out Func<MirServerPacket, ValueTask>? handler))
            return handler(packet);

        Unhandled?.Invoke(packet);
        return ValueTask.CompletedTask;
    }
}

