using System.Runtime.InteropServices;
using MirClient.Protocol.Text;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct MarketItem
{
    public ClientItem Item;
    public int UpgCount;
    public int Index;
    public int SellPrice;

    
    public fixed byte SellWho[21];

    
    public fixed byte SellDate[11];

    public ushort SellState;

    public string SellWhoString
    {
        get
        {
            fixed (byte* p = SellWho)
                return ReadShortString(p, 20);
        }
    }

    public string SellDateString
    {
        get
        {
            fixed (byte* p = SellDate)
                return ReadShortString(p, 10);
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

