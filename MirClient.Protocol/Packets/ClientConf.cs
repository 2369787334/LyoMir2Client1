using System.Runtime.InteropServices;

namespace MirClient.Protocol.Packets;




[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct ClientConf
{
    public byte ClientCanSet;
    public byte RunHuman;
    public byte RunMon;
    public byte RunNpc;
    public byte WarRunAll;
    public byte DieColor;
    public ushort SpellTime;
    public ushort HitTime;
    public ushort ItemFlashTime;
    public byte ItemSpeed;
    public byte CanStartRun;
    public byte ParalyCanRun;
    public byte ParalyCanWalk;
    public byte ParalyCanHit;
    public byte ParalyCanSpell;
    public byte ShowRedHpLabel;
    public byte ShowHpNumber;
    public byte ShowJobLevel;
    public byte DuraAlert;
    public byte MagicLock;
    public byte AutoPickUpItem;
}
