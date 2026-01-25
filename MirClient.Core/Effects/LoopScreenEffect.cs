namespace MirClient.Core.Effects;

public readonly record struct LoopScreenEffect(int Type, int X, int Y, long StartMs, long LastSeenMs);

