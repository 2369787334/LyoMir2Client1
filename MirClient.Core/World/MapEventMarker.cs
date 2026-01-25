namespace MirClient.Core.World;

public readonly record struct MapEventMarker(
    int Id,
    int X,
    int Y,
    int EventType,
    int EventParam,
    int EventLevel,
    int Dir,
    long StartTimestampMs);

