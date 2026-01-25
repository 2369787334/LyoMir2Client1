namespace MirClient.Assets.PackData;

public readonly record struct PackDataImageKey(string DataPath, int ImageIndex);

public sealed class PackDataImageKeyComparer : IEqualityComparer<PackDataImageKey>
{
    public static PackDataImageKeyComparer Instance { get; } = new();

    public bool Equals(PackDataImageKey x, PackDataImageKey y) =>
        x.ImageIndex == y.ImageIndex &&
        StringComparer.OrdinalIgnoreCase.Equals(x.DataPath, y.DataPath);

    public int GetHashCode(PackDataImageKey obj) =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.DataPath), obj.ImageIndex);
}

