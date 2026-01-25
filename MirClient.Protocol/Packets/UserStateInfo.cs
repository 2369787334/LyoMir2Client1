using System.Runtime.InteropServices;
using MirClient.Protocol.Text;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct UserStateInfoHeader
{
    public int Feature;

    
    public fixed byte UserName[16];

    public int NameColor;

    
    public fixed byte GuildName[15];

    
    public fixed byte GuildRankName[16];

    public byte Gender;
    public byte HumAttr;
    public byte Reserved1;
    public byte Reserved2;

    public string UserNameString
    {
        get
        {
            fixed (byte* p = UserName)
                return ReadShortString(p, 15);
        }
    }

    public string GuildNameString
    {
        get
        {
            fixed (byte* p = GuildName)
                return ReadShortString(p, 14);
        }
    }

    public string GuildRankNameString
    {
        get
        {
            fixed (byte* p = GuildRankName)
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
public struct HumTitle
{
    public byte Index;
    public int Time;
}

