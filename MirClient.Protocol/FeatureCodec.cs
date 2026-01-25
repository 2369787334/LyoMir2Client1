namespace MirClient.Protocol;




public static class FeatureCodec
{
    public static byte Race(int feature) => (byte)(feature & 0xFF);

    public static byte Weapon(int feature) => (byte)((feature >> 8) & 0xFF);

    public static byte Hair(int feature) => (byte)((feature >> 16) & 0xFF);

    public static byte Dress(int feature) => (byte)((feature >> 24) & 0xFF);

    public static ushort Appearance(int feature) => (ushort)((feature >> 16) & 0xFFFF);

    public static int MakeHumanFeature(byte raceImg, byte dress, byte weapon, byte hair) =>
        MakeLong(MakeWord(raceImg, weapon), MakeWord(hair, dress));

    public static int MakeMonsterFeature(byte raceImg, byte weapon, ushort appearance) =>
        MakeLong(MakeWord(raceImg, weapon), appearance);

    private static ushort MakeWord(byte low, byte high) => (ushort)((high << 8) | low);

    private static int MakeLong(ushort low, ushort high) => (high << 16) | low;
}

