using System.Runtime.InteropServices;
using MirClient.Protocol.Text;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct HeroInfo
{
    
    public fixed byte ChrName[15];

    public ushort Level;
    public byte Job;
    public byte Sex;

    public string ChrNameString
    {
        get
        {
            fixed (byte* p = ChrName)
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

