using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using MirClient.Protocol.Security;
using MirClient.Protocol.Text;

namespace MirClient.Core.Hardware;

[SupportedOSPlatform("windows")]
public static class MirHardwareId
{
    public static string GetHardwareIdString()
    {
        
        
        
        return string.Concat(
            GetIdeSerialNumber(),
            ReadBiosValue("BIOSVersion"),
            ReadBiosValue("BaseBoardProduct"),
            ReadBiosValue("BaseBoardSerialNumber"));
    }

    public static string CreateLoginToken(string? key = null)
    {
        string raw = GetHardwareIdString().Trim();
        if (string.IsNullOrEmpty(raw))
            raw = Environment.MachineName;

        byte[] bytes = GbkEncoding.Instance.GetBytes(raw);
        byte[] digest = MD5.HashData(bytes);
        return HardwareTokenCodec.Encode(digest, key: key ?? HardwareTokenCodec.DefaultKey);
    }

    private static string ReadBiosValue(string valueName)
    {
        try
        {
            using RegistryKey? key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            object? value = key?.GetValue(valueName);
            return value switch
            {
                null => string.Empty,
                string s => s.Trim(),
                string[] arr => string.Concat(arr).Trim(),
                _ => value.ToString()?.Trim() ?? string.Empty
            };
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string GetIdeSerialNumber()
    {
        string serial = TryGetPhysicalDrive0Serial();
        if (!string.IsNullOrWhiteSpace(serial))
            return serial.Trim();

        string volumeSerial = TryGetSystemVolumeSerial();
        return volumeSerial;
    }

    private static string TryGetPhysicalDrive0Serial()
    {
        const string physicalDrive0 = @"\\.\PhysicalDrive0";

        SafeFileHandle handle = CreateFile(
            physicalDrive0,
            GenericRead | GenericWrite,
            FileShareRead | FileShareWrite,
            IntPtr.Zero,
            OpenExisting,
            FileAttributeNormal,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            handle = CreateFile(
                physicalDrive0,
                GenericRead,
                FileShareRead | FileShareWrite,
                IntPtr.Zero,
                OpenExisting,
                FileAttributeNormal,
                IntPtr.Zero);
        }

        using (handle)
        {
            if (handle.IsInvalid)
                return string.Empty;

            
            Span<byte> inBuff = stackalloc byte[32];
            BinaryPrimitives.WriteUInt32LittleEndian(inBuff[..4], IdentifyBufferSize);

            
            inBuff[4 + 1] = 1; 
            inBuff[4 + 2] = 1; 
            inBuff[4 + 5] = 0xA0; 
            inBuff[4 + 6] = 0xEC; 

            byte[] outBuff = new byte[16 + IdentifyBufferSize]; 
            if (!DeviceIoControl(
                    handle,
                    SmartRcvDriveData,
                    inBuff.ToArray(),
                    inBuff.Length,
                    outBuff,
                    outBuff.Length,
                    out uint bytesReturned,
                    IntPtr.Zero))
            {
                return string.Empty;
            }

            if (bytesReturned < 16 + 40)
                return string.Empty;

            
            Span<byte> serialBytes = outBuff.AsSpan(16 + 20, 20);
            for (int i = 0; i + 1 < serialBytes.Length; i += 2)
                (serialBytes[i], serialBytes[i + 1]) = (serialBytes[i + 1], serialBytes[i]);

            return Encoding.ASCII.GetString(serialBytes).Trim('\0', ' ');
        }
    }

    private static string TryGetSystemVolumeSerial()
    {
        try
        {
            string root = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
            if (GetVolumeInformation(root, null, 0, out uint serial, out _, out _, null, 0))
                return serial.ToString("X8");
        }
        catch
        {
            
        }
        return string.Empty;
    }

    
    private const uint GenericRead = 0x80000000;
    private const uint GenericWrite = 0x40000000;
    private const uint FileShareRead = 0x00000001;
    private const uint FileShareWrite = 0x00000002;
    private const uint OpenExisting = 3;
    private const uint FileAttributeNormal = 0x00000080;

    private const uint SmartRcvDriveData = 0x0007C088;
    private const uint IdentifyBufferSize = 512;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFile(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(
        SafeFileHandle hDevice,
        uint dwIoControlCode,
        byte[] lpInBuffer,
        int nInBufferSize,
        byte[] lpOutBuffer,
        int nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool GetVolumeInformation(
        string lpRootPathName,
        StringBuilder? lpVolumeNameBuffer,
        int nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder? lpFileSystemNameBuffer,
        int nFileSystemNameSize);
}
