using System.Runtime.InteropServices;
using MirClient.Protocol.Text;

namespace MirClient.Protocol.Packets;

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public unsafe struct Magic
{
    public ushort MagicId;

    
    public fixed byte MagicName[13];

    public byte EffectType;
    public byte Effect;
    public byte Reserved0;
    public ushort Spell;
    public ushort Power;

    
    public fixed byte TrainLevel[4];
    public ushort Reserved1;
    public fixed int MaxTrain[4];

    public byte TrainLv;
    public byte Job;
    public ushort Reserved2;
    public int DelayTime;
    public byte DefSpell;
    public byte DefPower;
    public ushort MaxPower;
    public byte DefMaxPower;

    
    public fixed byte Descr[19];

    
    public byte Class => 0;

    public string MagicNameString
    {
        get
        {
            fixed (byte* p = MagicName)
            {
                int len = p[0];
                if (len <= 0)
                    return string.Empty;
                if (len > 12)
                    len = 12;

                return GbkEncoding.Instance.GetString(new ReadOnlySpan<byte>(p + 1, len));
            }
        }
    }

    public string DescrString
    {
        get
        {
            fixed (byte* p = Descr)
            {
                int len = p[0];
                if (len <= 0)
                    return string.Empty;
                if (len > 18)
                    len = 18;

                return GbkEncoding.Instance.GetString(new ReadOnlySpan<byte>(p + 1, len));
            }
        }
    }

    public byte GetTrainLevel(int index)
    {
        if ((uint)index >= 4u)
            return 0;

        fixed (byte* p = TrainLevel)
            return p[index];
    }

    public uint GetMaxTrain(int index)
    {
        if ((uint)index >= 4u)
            return 0;

        fixed (int* p = MaxTrain)
        {
            int value = p[index];
            return value <= 0 ? 0u : (uint)value;
        }
    }
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ClientMagic
{
    public char Key;
    public byte Level;
    public ushort Reserved;
    public int CurTrain;
    public Magic Def;

    public char KeyChar => Key;
}
