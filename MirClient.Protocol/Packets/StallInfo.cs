using System.Runtime.InteropServices;
using MirClient.Protocol.Text;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct StallInfo
{
    public byte Open;
    public ushort Looks;

    
    public fixed byte Name[29];

    public bool IsOpen => Open != 0;

    public string NameString
    {
        get
        {
            fixed (byte* p = Name)
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
}

