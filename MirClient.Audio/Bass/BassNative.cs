using System.Runtime.InteropServices;

namespace MirClient.Audio.Bass;

internal static class BassNative
{
    private const string DllName = "bass.dll";

    [Flags]
    internal enum BassInitFlags : uint
    {
        Default = 0,
    }

    [Flags]
    internal enum BassStreamFlags : uint
    {
        Default = 0,
        Loop = 0x0000_0004, 
        AutoFree = 0x0004_0000, 
        Unicode = 0x8000_0000, 
    }

    internal enum BassAttribute : uint
    {
        Frequency = 1, 
        Volume = 2, 
        Pan = 3, 
    }

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    internal static extern int BASS_ErrorGetCode();

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    internal static extern bool BASS_Init(int device, uint freq, BassInitFlags flags, IntPtr win, IntPtr clsid);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    internal static extern bool BASS_Free();

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    internal static extern bool BASS_Start();

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    internal static extern bool BASS_Stop();

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall, CharSet = CharSet.Unicode)]
    internal static extern int BASS_StreamCreateFile(bool mem, string file, long offset, long length, BassStreamFlags flags);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    internal static extern bool BASS_StreamFree(int handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    internal static extern bool BASS_ChannelPlay(int handle, bool restart);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    internal static extern bool BASS_ChannelStop(int handle);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    internal static extern bool BASS_ChannelSetAttribute(int handle, BassAttribute attrib, float value);

    [DllImport(DllName, CallingConvention = CallingConvention.StdCall)]
    internal static extern bool BASS_ChannelSlideAttribute(int handle, BassAttribute attrib, float value, uint time);
}
