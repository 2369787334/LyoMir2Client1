using System.Threading.Channels;

namespace MirClient.Core.Diagnostics;

public static class MirErrorLog
{
    private static int _installed;
    private static string? _logPath;
    private static Channel<string>? _channel;
    private static CancellationTokenSource? _cts;
    private static Task? _writerTask;

    public static void InstallFromEnvironment()
    {
        string? logPath = Environment.GetEnvironmentVariable("MIRCLIENT_ERROR_LOG_PATH");
        if (string.IsNullOrWhiteSpace(logPath))
        {
            logPath = Path.Combine(AppContext.BaseDirectory, "error.txt");
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
            throw new ArgumentException("Error log path is required.", nameof(logPath));

        logPath = logPath.Trim();
        if (!Path.IsPathRooted(logPath))
            logPath = Path.Combine(AppContext.BaseDirectory, logPath);

        _logPath = logPath;
        _cts = new CancellationTokenSource();
        _channel = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = true
        });

        _writerTask = Task.Run(() => WriterLoopAsync(_channel.Reader, logPath, _cts.Token));

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex)
                WriteException("UnhandledException", ex, isTerminating: e.IsTerminating);
            else
                Write($"[UnhandledException] terminating={e.IsTerminating} {e.ExceptionObject}");
        };

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteException("UnobservedTaskException", e.Exception);
        };

        AppDomain.CurrentDomain.ProcessExit += (_, _) => Shutdown(flushTimeoutMs: 250);
    }

    public static void Write(string message)
    {
        if (_channel == null)
            return;

        if (string.IsNullOrWhiteSpace(message))
            return;

        string line = $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";
        _channel.Writer.TryWrite(line);
    }

    public static void WriteException(string source, Exception exception, bool? isTerminating = null)
    {
        if (exception == null)
            return;

        string terminating = isTerminating.HasValue ? $" terminating={isTerminating.Value}" : string.Empty;
        Write($"[{source}]{terminating}{Environment.NewLine}{exception}");
    }

    private static async Task WriterLoopAsync(ChannelReader<string> reader, string logPath, CancellationToken token)
    {
        try
        {
            string? dir = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            using var stream = new FileStream(logPath, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            using var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };

            await foreach (string line in reader.ReadAllAsync(token).ConfigureAwait(false))
            {
                try
                {
                    await writer.WriteLineAsync(line).ConfigureAwait(false);
                }
                catch
                {
                    
                }
            }
        }
        catch
        {
            
        }
    }

    private static void Shutdown(int flushTimeoutMs)
    {
        try
        {
            _cts?.Cancel();
            _channel?.Writer.TryComplete();

            Task? writerTask = _writerTask;
            if (writerTask != null && flushTimeoutMs > 0)
            {
                try { writerTask.Wait(Math.Max(1, flushTimeoutMs)); } catch {  }
            }
        }
        catch
        {
            
        }
    }
}

