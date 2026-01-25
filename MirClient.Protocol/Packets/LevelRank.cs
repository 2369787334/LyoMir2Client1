using System.Runtime.InteropServices;
using MirClient.Protocol.Text;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct HumanLevelRank
{
    
    public fixed byte CharName[16];

    public int Level;
    public int Index;

    public string CharNameString
    {
        get
        {
            fixed (byte* p = CharName)
                return ReadShortString(p, 15);
        }
    }

    private static string ReadShortString(byte* buffer, int maxLen)
    {
        int len = buffer[0];
        if (len <= 0)
            return string.Empty;
        if (len > maxLen)
            len = maxLen;

        return GbkEncoding.Instance.GetString(new ReadOnlySpan<byte>(buffer + 1, len));
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct HeroLevelRank
{
    
    public fixed byte MasterName[15];

    
    public fixed byte HeroName[15];

    public ushort Level;
    public ushort Index;

    public string MasterNameString
    {
        get
        {
            fixed (byte* p = MasterName)
                return ReadShortString(p, 14);
        }
    }

    public string HeroNameString
    {
        get
        {
            fixed (byte* p = HeroName)
                return ReadShortString(p, 14);
        }
    }

    private static string ReadShortString(byte* buffer, int maxLen)
    {
        int len = buffer[0];
        if (len <= 0)
            return string.Empty;
        if (len > maxLen)
            len = maxLen;

        return GbkEncoding.Instance.GetString(new ReadOnlySpan<byte>(buffer + 1, len));
    }
}

