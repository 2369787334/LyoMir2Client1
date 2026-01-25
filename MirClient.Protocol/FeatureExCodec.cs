namespace MirClient.Protocol;




public static class FeatureExCodec
{
    public static byte Horse(int featureEx) => (byte)(featureEx & 0xFF);

    public static byte Effect(int featureEx) => (byte)((featureEx >> 8) & 0xFF);
}

