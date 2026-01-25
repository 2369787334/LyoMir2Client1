namespace MirClient.Assets.PackData;

public readonly record struct PackDataImageInfo(
    int Width,
    int Height,
    byte BitCount,
    short Px,
    short Py,
    uint CompressedLength,
    PackDataGraphicType GraphicType);

