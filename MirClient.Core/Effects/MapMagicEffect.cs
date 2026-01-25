namespace MirClient.Core.Effects;

public readonly record struct MapMagicEffect(int EffectNumber, int EffectType, int X, int Y, long StartMs)
{
    public const int DefaultFrames = 10;
    public const int DefaultFrameTimeMs = 50;
    public const int DefaultDurationMs = DefaultFrames * DefaultFrameTimeMs;
}
