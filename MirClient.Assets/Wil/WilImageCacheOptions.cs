namespace MirClient.Assets.Wil;

public sealed record WilImageCacheOptions(
    long ImageCacheBytes = 256L * 1024 * 1024,
    int MaxConcurrentDecodes = 0);
