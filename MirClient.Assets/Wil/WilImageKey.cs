namespace MirClient.Assets.Wil;

public readonly record struct WilImageKey(string WilPath, int ImageIndex);

public sealed class WilImageKeyComparer : IEqualityComparer<WilImageKey>
{
    public static WilImageKeyComparer Instance { get; } = new();

    public bool Equals(WilImageKey x, WilImageKey y) =>
        x.ImageIndex == y.ImageIndex &&
        StringComparer.OrdinalIgnoreCase.Equals(x.WilPath, y.WilPath);

    public int GetHashCode(WilImageKey obj) =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.WilPath), obj.ImageIndex);
}

