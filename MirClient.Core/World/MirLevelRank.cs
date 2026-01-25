namespace MirClient.Core.World;

public readonly record struct MirHumanLevelRank(string Name, int Level, int Index);

public readonly record struct MirHeroLevelRank(string MasterName, string HeroName, int Level, int Index);

