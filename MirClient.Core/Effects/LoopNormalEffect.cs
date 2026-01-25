namespace MirClient.Core.Effects;

public readonly record struct LoopNormalEffect(int Type, int X, int Y, long StartMs, long LastSeenMs);

