using System.Runtime.InteropServices;

namespace MirClient.Protocol.Packets;




[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct Ability
{
    public ushort Level;
    public int AC;
    public int MAC;
    public int DC;
    public int MC;
    public int SC;
    public ushort HP;
    public ushort MP;
    public ushort MaxHP;
    public ushort MaxMP;
    public int Exp;
    public int MaxExp;
    public ushort Weight;
    public ushort MaxWeight;
    public ushort WearWeight;
    public ushort MaxWearWeight;
    public ushort HandWeight;
    public ushort MaxHandWeight;
}

[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct OldAbility
{
    public ushort Level;
    public ushort AC;
    public ushort MAC;
    public ushort DC;
    public ushort MC;
    public ushort SC;
    public ushort HP;
    public ushort MP;
    public ushort MaxHP;
    public ushort MaxMP;
    public ushort Diamond;
    public ushort Gird;
    public uint Exp;
    public uint MaxExp;
    public ushort Weight;
    public ushort MaxWeight;
    public byte WearWeight;
    public byte MaxWearWeight;
    public byte HandWeight;
    public byte MaxHandWeight;
}

