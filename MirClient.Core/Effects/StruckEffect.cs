namespace MirClient.Core.Effects;

public readonly record struct StruckEffect(int ActorId, int Type, int Tag, long StartMs);

