using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace MirClient.Platform;

internal static class HangDump
{
    [Flags]
    private enum MiniDumpType : uint
    {
        MiniDumpNormal = 0x00000000,
        MiniDumpWithDataSegs = 0x00000001,
        MiniDumpWithFullMemory = 0x00000002,
        MiniDumpWithHandleData = 0x00000004,
        MiniDumpWithUnloadedModules = 0x00000020,
        MiniDumpWithProcessThreadData = 0x00000100,
        MiniDumpWithFullMemoryInfo = 0x00000800,
        MiniDumpWithThreadInfo = 0x00001000,
        MiniDumpWithTokenInformation = 0x00004000
    }

    [DllImport("dbghelp.dll", SetLastError = true)]
    private static extern bool MiniDumpWriteDump(
        IntPtr hProcess,
        int processId,
        SafeFileHandle hFile,
        MiniDumpType dumpType,
        IntPtr exceptionParam,
        IntPtr userStreamParam,
        IntPtr callbackParam);

    public static bool TryWrite(string dumpPath, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(dumpPath))
        {
            error = "Dump path is required.";
            return false;
        }

        try
        {
            using Process process = Process.GetCurrentProcess();
            using FileStream fs = new(dumpPath, FileMode.Create, FileAccess.Write, FileShare.None);

            MiniDumpType type =
                MiniDumpType.MiniDumpWithThreadInfo |
                MiniDumpType.MiniDumpWithUnloadedModules |
                MiniDumpType.MiniDumpWithHandleData |
                MiniDumpType.MiniDumpWithProcessThreadData |
                MiniDumpType.MiniDumpWithTokenInformation |
                MiniDumpType.MiniDumpWithFullMemoryInfo;

            bool ok = MiniDumpWriteDump(
                process.Handle,
                process.Id,
                fs.SafeFileHandle,
                type,
                IntPtr.Zero,
                IntPtr.Zero,
                IntPtr.Zero);

            if (!ok)
            {
                error = new Win32Exception(Marshal.GetLastWin32Error()).Message;
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }
}

