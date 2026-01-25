namespace MirClient.Core.Effects;

public abstract class MirEffect
{
    public int X { get; protected set; }
    public int Y { get; protected set; }

    public virtual bool Tick(long nowMs) => true;
}

