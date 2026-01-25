namespace MirClient.Assets.PackData;

public sealed record PackDataImageCacheOptions(
    long ImageCacheBytes = 256L * 1024 * 1024,
    int MaxConcurrentDecodes = 0);
