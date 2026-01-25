namespace MirClient.Core.Diagnostics;

public static class MirCrashLog
{
    private static int _installed;

    public static void InstallFromEnvironment()
    {
        string? logPath = Environment.GetEnvironmentVariable("MIRCLIENT_CRASH_LOG_PATH");
        if (string.IsNullOrWhiteSpace(logPath))
        {
            logPath = Path.Combine(AppContext.BaseDirectory, "logs", "crash.log");
        }
        else
        {
            logPath = logPath.Trim();
            if (!Path.IsPathRooted(logPath))
                logPath = Path.Combine(AppContext.BaseDirectory, logPath);
        }

        Install(logPath);
    }

    public static void Install(string logPath)
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        if (string.IsNullOrWhiteSpace(logPath))
            throw new ArgumentException("Crash log path is required.", nameof(logPath));

        logPath = logPath.Trim();
        if (!Path.IsPathRooted(logPath))
            logPath = Path.Combine(AppContext.BaseDirectory, logPath);

        void Write(string source, Exception? exception, bool? isTerminating = null)
        {
            try
            {
                string? dir = Path.GetDirectoryName(logPath);
                if (!string.IsNullOrWhiteSpace(dir))
                    Directory.CreateDirectory(dir);

                string terminating = isTerminating.HasValue ? $" terminating={isTerminating.Value}" : string.Empty;
                string text = $"[{DateTimeOffset.UtcNow:O}] {source}{terminating}{Environment.NewLine}{exception}{Environment.NewLine}{Environment.NewLine}";
                File.AppendAllText(logPath, text);
            }
            catch
            {
            }
        }

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Write("UnhandledException", e.ExceptionObject as Exception, e.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, e) =>
            Write("UnobservedTaskException", e.Exception);
    }
}

