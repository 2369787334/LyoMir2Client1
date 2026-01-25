namespace MirClient.Assets.Caching;

public readonly record struct LruCacheStats(int Count, long BudgetWeight, long CurrentWeight, long Hits, long Misses);

