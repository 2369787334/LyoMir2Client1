using System.Runtime.InteropServices;
using MirClient.Protocol.Text;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ShopItem
{
    
    public fixed byte ItemName[21];

    public byte Class;
    public ushort Looks;
    public uint Price;
    public ushort Shape1;
    public ushort Shape2;

    
    public fixed byte Explain[128];

    public string ItemNameString
    {
        get
        {
            fixed (byte* p = ItemName)
            {
                int len = p[0];
                if (len <= 0)
                    return string.Empty;
                if (len > 20)
                    len = 20;

                return GbkEncoding.Instance.GetString(new ReadOnlySpan<byte>(p + 1, len));
            }
        }
    }

    public string ExplainString
    {
        get
        {
            fixed (byte* p = Explain)
            {
                int len = p[0];
                if (len <= 0)
                    return string.Empty;
                if (len > 127)
                    len = 127;

                return GbkEncoding.Instance.GetString(new ReadOnlySpan<byte>(p + 1, len));
            }
        }
    }
}

