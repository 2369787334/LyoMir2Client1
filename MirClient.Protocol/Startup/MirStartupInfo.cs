using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using MirClient.Protocol.Text;

namespace MirClient.Protocol.Startup;

public sealed record MirStartupInfo(
    string ServerName,
    string ServerAddress,
    string ServerKey,
    string UiPakKey,
    string ResourceDir,
    int ServerPort,
    bool FullScreen,
    bool WaitVBlank,
    bool Use3D,
    bool Mini,
    int ScreenWidth,
    int ScreenHeight,
    int LocalMiniPort,
    byte ClientVersion,
    string Logo,
    string PasswordFileName)
{
    public static unsafe MirStartupInfo FromRaw(in MirStartupInfoRaw raw)
    {
        fixed (byte* pServerName = raw.sServerName)
        fixed (byte* pServerAddr = raw.sServeraddr)
        fixed (byte* pServerKey = raw.sServerKey)
        fixed (byte* pUiPakKey = raw.sUIPakKey)
        fixed (byte* pResourceDir = raw.sResourceDir)
        fixed (byte* pLogo = raw.sLogo)
        fixed (byte* pPasswordFile = raw.PassWordFileName)
        {
            return new MirStartupInfo(
                ServerName: ReadShortString(pServerName, 30),
                ServerAddress: ReadShortString(pServerAddr, 30),
                ServerKey: ReadShortString(pServerKey, 100),
                UiPakKey: ReadShortString(pUiPakKey, 32),
                ResourceDir: ReadShortString(pResourceDir, 50),
                ServerPort: raw.nServerPort,
                FullScreen: raw.boFullScreen != 0,
                WaitVBlank: raw.boWaitVBlank != 0,
                Use3D: raw.bo3D != 0,
                Mini: raw.boMini != 0,
                ScreenWidth: raw.nScreenWidth,
                ScreenHeight: raw.nScreenHegiht,
                LocalMiniPort: raw.nLocalMiniPort,
                ClientVersion: raw.btClientVer,
                Logo: ReadShortString(pLogo, 255),
                PasswordFileName: ReadShortString(pPasswordFile, 127));
        }
    }

    public static MirStartupInfo DecodeClientParamStr(string clientParamStr)
    {
        byte[] plain = LauncherParamCodec.DecodeSourceData(clientParamStr);
        int requiredSize = Unsafe.SizeOf<MirStartupInfoRaw>();
        if (plain.Length < requiredSize)
            throw new FormatException($"Decoded startup info is too short: {plain.Length} < {requiredSize}.");

        var raw = MemoryMarshal.Read<MirStartupInfoRaw>(plain.AsSpan(0, requiredSize));
        return FromRaw(raw);
    }

    private static unsafe string ReadShortString(byte* ptr, int maxLen)
    {
        if (ptr == null)
            return string.Empty;

        int storedLen = ptr[0];
        if (storedLen <= 0)
            return string.Empty;
        if (storedLen > maxLen)
            storedLen = maxLen;

        return GbkEncoding.Instance.GetString(new ReadOnlySpan<byte>(ptr + 1, storedLen));
    }
}
