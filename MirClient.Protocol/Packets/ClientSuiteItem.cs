using System.Runtime.InteropServices;
using MirClient.Protocol.Text;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ClientSuiteItem
{
    
    public const int SuiteNameCount = 13;
    public const int SuiteNameMaxLen = 20;
    private const int SuiteNameSlotSize = 1 + SuiteNameMaxLen;
    public const int SuitSubRateCount = 40; 

    public byte Gender;
    public byte ItemColor;
    public byte AbilColor;
    public int NeedCount;

    public fixed ushort SuitSubRate[SuitSubRateCount];

    
    public fixed byte SuiteNames[SuiteNameCount * SuiteNameSlotSize];

    public ushort GetSuitSubRate(int index)
    {
        if ((uint)index >= SuitSubRateCount)
            return 0;

        fixed (ushort* p = SuitSubRate)
            return p[index];
    }

    public string GetSuiteName(int index)
    {
        if ((uint)index >= SuiteNameCount)
            return string.Empty;

        fixed (byte* p = SuiteNames)
        {
            byte* entry = p + (index * SuiteNameSlotSize);
            int len = entry[0];
            if (len <= 0)
                return string.Empty;
            if (len > SuiteNameMaxLen)
                len = SuiteNameMaxLen;

            return GbkEncoding.Instance.GetString(new ReadOnlySpan<byte>(entry + 1, len));
        }
    }
}

