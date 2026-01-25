using System.Runtime.InteropServices;
using MirClient.Protocol.Text;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ClientStallItems
{
    public const int MaxStallItemCount = 10;
    private const int StallNameMaxLen = 28;

    
    public fixed byte Name[StallNameMaxLen + 1];

    public ClientStall Item0;
    public ClientStall Item1;
    public ClientStall Item2;
    public ClientStall Item3;
    public ClientStall Item4;
    public ClientStall Item5;
    public ClientStall Item6;
    public ClientStall Item7;
    public ClientStall Item8;
    public ClientStall Item9;

    public string NameString
    {
        get
        {
            fixed (byte* p = Name)
                return ReadShortString(p, StallNameMaxLen);
        }
    }

    public void SetNameString(string name)
    {
        fixed (byte* p = Name)
            WriteShortString(p, StallNameMaxLen, name);
    }

    public ClientStall GetItem(int index)
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

    public void SetItem(int index, ClientStall item)
    {
        switch (index)
        {
            case 0: Item0 = item; break;
            case 1: Item1 = item; break;
            case 2: Item2 = item; break;
            case 3: Item3 = item; break;
            case 4: Item4 = item; break;
            case 5: Item5 = item; break;
            case 6: Item6 = item; break;
            case 7: Item7 = item; break;
            case 8: Item8 = item; break;
            case 9: Item9 = item; break;
        }
    }

    private static unsafe string ReadShortString(byte* buffer, int maxLen)
    {
        if (buffer == null)
            return string.Empty;

        int len = buffer[0];
        if (len <= 0)
            return string.Empty;
        if (len > maxLen)
            len = maxLen;

        return GbkEncoding.Instance.GetString(new ReadOnlySpan<byte>(buffer + 1, len));
    }

    private static unsafe void WriteShortString(byte* buffer, int maxLen, string value)
    {
        if (buffer == null)
            return;

        buffer[0] = 0;
        new Span<byte>(buffer + 1, maxLen).Clear();

        if (string.IsNullOrWhiteSpace(value))
            return;

        byte[] bytes = GbkEncoding.Instance.GetBytes(value.Trim());
        int len = Math.Min(maxLen, bytes.Length);
        if (len <= 0)
            return;

        buffer[0] = (byte)len;
        bytes.AsSpan(0, len).CopyTo(new Span<byte>(buffer + 1, maxLen));
    }
}

