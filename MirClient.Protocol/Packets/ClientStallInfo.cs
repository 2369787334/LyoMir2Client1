using System.Runtime.InteropServices;
using MirClient.Protocol.Text;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ClientStallInfo
{
    public const int MaxStallItemCount = 10;

    public int ItemCount;

    
    public fixed byte StallName[29];

    public ClientItem Item0;
    public ClientItem Item1;
    public ClientItem Item2;
    public ClientItem Item3;
    public ClientItem Item4;
    public ClientItem Item5;
    public ClientItem Item6;
    public ClientItem Item7;
    public ClientItem Item8;
    public ClientItem Item9;

    public string StallNameString
    {
        get
        {
            fixed (byte* p = StallName)
            {
                int len = p[0];
                if (len <= 0)
                    return string.Empty;
                if (len > 28)
                    len = 28;

                return GbkEncoding.Instance.GetString(new ReadOnlySpan<byte>(p + 1, len));
            }
        }
    }

    public ClientItem GetItem(int index)
    {
        return index switch
        {
            0 => Item0,
            1 => Item1,
            2 => Item2,
            3 => Item3,
            4 => Item4,
            5 => Item5,
            6 => Item6,
            7 => Item7,
            8 => Item8,
            9 => Item9,
            _ => default
        };
    }
}

