using System.Runtime.InteropServices;

namespace MirClient.Protocol.Startup;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public unsafe struct MirStartupInfoRaw
{
    public fixed byte sServerName[31]; 
    public fixed byte sServeraddr[31]; 
    public fixed byte sServerKey[101]; 
    public fixed byte sUIPakKey[33]; 
    public fixed byte sResourceDir[51]; 

    public int nServerPort;
    public byte boFullScreen;
    public byte boWaitVBlank;
    public byte bo3D;
    public byte boMini;

    public int nScreenWidth;
    public int nScreenHegiht;
    public int nLocalMiniPort;
    public byte btClientVer;

    public fixed byte sLogo[256]; 
    public fixed byte PassWordFileName[128]; 
}
