using System.Runtime.InteropServices;
using System.Reflection;

namespace MirClient.Platform;

internal static class WindowsFirewall
{
    private const int NET_FW_SCOPE_ALL = 0;
    private const int NET_FW_IP_VERSION_ANY = 2;
    private const int NET_FW_IP_PROTOCOL_TCP = 6;

    public static void TryAddApplicationToFirewall(string entryName, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return;

        object? mgr = null;
        object? application = null;

        try
        {
            mgr = TryCreateCom("HNetCfg.FwMgr");
            application = TryCreateCom("HNetCfg.FwAuthorizedApplication");
            if (mgr == null || application == null)
                return;

            try
            {
                SetComProperty(application, "ProcessImageFileName", fileName);
                SetComProperty(application, "Name", entryName);
                SetComProperty(application, "Scope", NET_FW_SCOPE_ALL);
                SetComProperty(application, "IpVersion", NET_FW_IP_VERSION_ANY);
                SetComProperty(application, "Enabled", true);

                object? localPolicy = GetComProperty(mgr, "LocalPolicy");
                object? currentProfile = localPolicy == null ? null : GetComProperty(localPolicy, "CurrentProfile");
                object? authorizedApplications = currentProfile == null ? null : GetComProperty(currentProfile, "AuthorizedApplications");

                if (authorizedApplications != null)
                {
                    try
                    {
                        InvokeComMethod(authorizedApplications, "Remove", fileName);
                    }
                    catch
                    {
                    }

                    InvokeComMethod(authorizedApplications, "Add", application);
                }
            }
            catch
            {
            }
        }
        catch
        {
        }
        finally
        {
            ReleaseComObject(application);
            ReleaseComObject(mgr);
        }
    }

    public static void TryAddPortToFirewall(string entryName, int portNumber)
    {
        if (portNumber <= 0 || portNumber > 65535)
            return;

        object? mgr = null;
        object? port = null;

        try
        {
            mgr = TryCreateCom("HNetCfg.FwMgr");
            port = TryCreateCom("HNetCfg.FWOpenPort");
            if (mgr == null || port == null)
                return;

            SetComProperty(port, "Name", entryName);
            SetComProperty(port, "Protocol", NET_FW_IP_PROTOCOL_TCP);
            SetComProperty(port, "Port", portNumber);
            SetComProperty(port, "Scope", NET_FW_SCOPE_ALL);
            SetComProperty(port, "Enabled", true);

            object? localPolicy = GetComProperty(mgr, "LocalPolicy");
            object? currentProfile = localPolicy == null ? null : GetComProperty(localPolicy, "CurrentProfile");
            object? globallyOpenPorts = currentProfile == null ? null : GetComProperty(currentProfile, "GloballyOpenPorts");
            if (globallyOpenPorts != null)
                InvokeComMethod(globallyOpenPorts, "Add", port);
        }
        catch
        {
        }
        finally
        {
            ReleaseComObject(port);
            ReleaseComObject(mgr);
        }
    }

    private static object? TryCreateCom(string progId)
    {
        try
        {
            Type? type = Type.GetTypeFromProgID(progId, throwOnError: false);
            return type == null ? null : Activator.CreateInstance(type);
        }
        catch
        {
            return null;
        }
    }

    private static void ReleaseComObject(object? obj)
    {
        if (obj == null)
            return;

        try
        {
            if (Marshal.IsComObject(obj))
                Marshal.FinalReleaseComObject(obj);
        }
        catch
        {
        }
    }

    private static object? GetComProperty(object obj, string propertyName)
    {
        return obj.GetType().InvokeMember(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.GetProperty,
            binder: null,
            target: obj,
            args: null);
    }

    private static void SetComProperty(object obj, string propertyName, object? value)
    {
        obj.GetType().InvokeMember(
            propertyName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.SetProperty,
            binder: null,
            target: obj,
            args: new[] { value });
    }

    private static object? InvokeComMethod(object obj, string methodName, params object?[] args)
    {
        return obj.GetType().InvokeMember(
            methodName,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.InvokeMethod,
            binder: null,
            target: obj,
            args: args);
    }
}
