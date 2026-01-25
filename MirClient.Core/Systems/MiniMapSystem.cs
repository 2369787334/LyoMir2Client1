namespace MirClient.Core.Systems;

public sealed class MiniMapSystem
{
    private const long RequestCooldownMs = 3000;
    private const long BlinkIntervalMs = 300;

    public int ViewLevel { get; private set; }
    public long LastRequestMs { get; private set; }
    public long LastBlinkMs { get; private set; }
    public bool BlinkOn { get; private set; }

    public void Reset()
    {
        ViewLevel = 0;
        LastRequestMs = 0;
        LastBlinkMs = 0;
        BlinkOn = false;
    }

    public void Tick(long nowMs)
    {
        if (ViewLevel <= 0)
            return;

        if (nowMs - LastBlinkMs < BlinkIntervalMs)
            return;

        LastBlinkMs = nowMs;
        BlinkOn = !BlinkOn;
    }

    public MiniMapToggleResult Toggle(long nowMs)
    {
        if (ViewLevel <= 0)
        {
            if (nowMs - LastRequestMs < RequestCooldownMs)
                return new MiniMapToggleResult(ViewLevel, Request: false);

            LastRequestMs = nowMs;
            ViewLevel = 1;
            return new MiniMapToggleResult(ViewLevel, Request: true);
        }

        if (ViewLevel >= 2)
        {
            ViewLevel = 0;
            return new MiniMapToggleResult(ViewLevel, Request: false);
        }

        ViewLevel++;
        return new MiniMapToggleResult(ViewLevel, Request: false);
    }

    public readonly record struct MiniMapToggleResult(int ViewLevel, bool Request);
}
