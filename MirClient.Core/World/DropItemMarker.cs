namespace MirClient.Core.World;

public readonly record struct DropItemMarker(int Id, int X, int Y, int Looks, string Name, long SpawnTimestampMs);

