using System.Runtime.InteropServices;
using MirClient.Protocol.Text;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public readonly record struct EvaAbil(byte Type, byte Value);

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Evaluation
{
    public byte EvaTimes;
    public byte EvaTimesMax;

    public byte AdvAbil;
    public byte AdvAbilMax;

    public byte Spirit;
    public byte SpiritMax;

    
    public fixed byte Abil[16];

    public byte BaseMax;
    public byte Quality;
    public byte SpiritQ;
    public byte SpSkill;

    public EvaAbil GetAbil(int index)
    {
        if ((uint)index >= 8u)
            return default;

        fixed (byte* p = Abil)
        {
            int offset = index * 2;
            return new EvaAbil(p[offset], p[offset + 1]);
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ClientStdItem
{
    
    public fixed byte Name[21];

    public byte StdMode;
    public byte Shape;
    public byte Weight;
    public ushort AniCount;
    public sbyte Source;
    public byte Reserved;
    public byte NeedIdentify;
    public ushort Looks;
    public ushort DuraMax;
    public int AC;
    public int MAC;
    public int DC;
    public int MC;
    public int SC;
    public int Need;
    public int NeedLevel;
    public int Price;

    public byte UniqueItem;
    public byte Overlap;
    public byte ItemType;
    public ushort ItemSet;

    public byte Binded;
    public fixed byte Reserve[9];
    public fixed byte AddOn[10];
    public Evaluation Eva;

    public string NameString
    {
        get
        {
            fixed (byte* p = Name)
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
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct ClientItem
{
    public ClientStdItem S;
    public int MakeIndex;
    public ushort Dura;
    public ushort DuraMax;

    public string NameString => S.NameString;
}

