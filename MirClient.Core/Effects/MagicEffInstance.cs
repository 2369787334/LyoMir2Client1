namespace MirClient.Core.Effects;

public readonly record struct MagicEffInstance(
    byte EffectNumber,
    byte EffectType,
    int FromX,
    int FromY,
    int ToX,
    int ToY,
    int TargetActorId,
    int MagicLevel,
    long StartMs,
    byte Dir16,
    int TravelDurationMs);
