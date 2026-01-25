using MirClient.Platform;
using MirClient.Core.Diagnostics;

namespace MirClient
{
    internal static class Program
    {
        
        
        
        [STAThread]
        static void Main(string[] args)
        {
            
            
            ApplicationConfiguration.Initialize();
            Core.Diagnostics.MirCrashLog.InstallFromEnvironment();
            MirErrorLog.InstallFromEnvironment();

            Application.ThreadException += (_, e) =>
            {
                if (e.Exception != null)
                    MirErrorLog.WriteException("ThreadException", e.Exception);
            };

            WindowsFirewall.TryAddApplicationToFirewall("MirClient", Environment.ProcessPath ?? string.Empty);
            Application.Run(new Main(args));
        }
    }
}
