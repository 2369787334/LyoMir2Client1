namespace MirClient.Assets.PackData;

public readonly record struct PackDataHeader(
    string Title,
    int ImageCount,
    int IndexOffset,
    ushort XVersion,
    string Password)
{
    public bool IsEncrypted => XVersion == 1;
}

