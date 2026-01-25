namespace MirClient.Assets.Palettes;

public static class Mir2ColorTable
{
    public static ReadOnlySpan<uint> Colors => Mir2MainPalette.Colors;

    public static uint GetArgb(byte index)
    {
        ReadOnlySpan<uint> colors = Mir2MainPalette.Colors;
        return index < colors.Length ? colors[index] : 0xFFFFFFFFu;
    }
}

