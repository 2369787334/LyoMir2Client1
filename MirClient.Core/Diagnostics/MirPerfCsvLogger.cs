using System.Diagnostics;
using System.Globalization;
using System.Text;
using MirClient.Core;

namespace MirClient.Core.Diagnostics;

public readonly record struct MirPerfCacheStats(int Count, long CurrentBytes, long BudgetBytes, long Hits, long Misses);

public readonly record struct MirPerfCsvSample(
    MirSessionStage Stage,
    string MapTitle,
    int? CenterX,
    int? CenterY,
    int ActorCount,
    double AvgFps,
    double AvgFrameMs,
    double AvgCpuMs,
    double LastFrameMs,
    double LastCpuMs,
    int DrawCalls,
    int TextureBinds,
    int Sprites,
    int ScissorChanges,
    bool VSync,
    int TargetFps,
    MirPerfCacheStats CpuWil,
    MirPerfCacheStats CpuData,
    MirPerfCacheStats? GpuWil,
    MirPerfCacheStats? GpuData,
    int BackBufferWidth,
    int BackBufferHeight,
    int LogicalWidth,
    int LogicalHeight,
    float ViewScaleX,
    float ViewScaleY,
    float ViewOffsetX,
    float ViewOffsetY);

public readonly record struct MirPerfCsvLoggerOptions(
    string Path,
    int IntervalMs,
    int HitchThresholdMs,
    double BudgetMaxFrameMs,
    double BudgetMaxCpuMs,
    double BudgetMinAvgFps,
    int BudgetMaxDrawCalls,
    int BudgetMaxTextureBinds,
    int BudgetMaxGpuMb);

public sealed class MirPerfCsvLogger : IDisposable
{
    private static readonly string Header =
        "timeUtc,uptimeMs,event,stage,mapTitle,centerX,centerY,actors," +
        "avgFps,avgFrameMs,avgCpuMs,lastFrameMs,lastCpuMs," +
        "drawCalls,texBinds,sprites,scissorChanges,vsync,targetFps," +
        "cpuWilCount,cpuWilMb,cpuWilBudgetMb,cpuWilHits,cpuWilMisses," +
        "cpuDataCount,cpuDataMb,cpuDataBudgetMb,cpuDataHits,cpuDataMisses," +
        "gpuWilCount,gpuWilMb,gpuWilBudgetMb,gpuWilHits,gpuWilMisses," +
        "gpuDataCount,gpuDataMb,gpuDataBudgetMb,gpuDataHits,gpuDataMisses," +
        "gpuTotalCount,gpuTotalMb,gpuTotalBudgetMb," +
        "backBufferW,backBufferH,logicalW,logicalH,viewScaleX,viewScaleY,viewOffsetX,viewOffsetY," +
        "gcTotalMb,gcGen0,gcGen1,gcGen2,procWorkingSetMb,procPrivateMb," +
        "hitchThresholdMs,hitchCount,maxFrameMs," +
        "budgetMaxFrameMs,budgetMaxCpuMs,budgetMinAvgFps,budgetMaxDrawCalls,budgetMaxTexBinds,budgetMaxGpuMb,budgetReasons";

    [Flags]
    private enum BudgetViolation
    {
        None = 0,
        AvgFpsLow = 1 << 0,
        FrameMsHigh = 1 << 1,
        CpuMsHigh = 1 << 2,
        DrawCallsHigh = 1 << 3,
        TextureBindsHigh = 1 << 4,
        GpuMbHigh = 1 << 5
    }

    private readonly StringBuilder _lineBuilder = new(1024);
    private readonly Action<string>? _log;
    private StreamWriter? _writer;
    private long _nextWriteMs;
    private bool _hitchActive;
    private int _hitchCount;
    private double _maxFrameMs;
    private bool _budgetActive;
    private BudgetViolation _budgetViolations;
    private bool _disabled;

    public MirPerfCsvLoggerOptions Options { get; }

    public MirPerfCsvLogger(MirPerfCsvLoggerOptions options, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(options.Path))
            throw new ArgumentException("Perf log path is required.", nameof(options));

        Options = options;
        _log = log;
        _nextWriteMs = Environment.TickCount64;
    }

    public static MirPerfCsvLogger? TryCreateFromEnvironment(Action<string>? log = null)
    {
        int intervalMs = 5000;
        if (int.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_PERF_LOG_INTERVAL_MS"), out int parsedInterval) &&
            parsedInterval is >= 0 and <= 600_000)
        {
            intervalMs = parsedInterval;
        }

        int hitchMs = 200;
        if (int.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_PERF_HITCH_MS"), out int parsedHitch) &&
            parsedHitch is >= 1 and <= 60_000)
        {
            hitchMs = parsedHitch;
        }

        string? path = Environment.GetEnvironmentVariable("MIRCLIENT_PERF_LOG_PATH");
        if (string.IsNullOrWhiteSpace(path))
            return null;

        double budgetMaxFrameMs = 0;
        if (double.TryParse(
                Environment.GetEnvironmentVariable("MIRCLIENT_PERF_BUDGET_MAX_FRAME_MS"),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsedBudgetMaxFrameMs) &&
            parsedBudgetMaxFrameMs is >= 0 and <= 60_000)
        {
            budgetMaxFrameMs = parsedBudgetMaxFrameMs;
        }

        double budgetMaxCpuMs = 0;
        if (double.TryParse(
                Environment.GetEnvironmentVariable("MIRCLIENT_PERF_BUDGET_MAX_CPU_MS"),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsedBudgetMaxCpuMs) &&
            parsedBudgetMaxCpuMs is >= 0 and <= 60_000)
        {
            budgetMaxCpuMs = parsedBudgetMaxCpuMs;
        }

        double budgetMinAvgFps = 0;
        if (double.TryParse(
                Environment.GetEnvironmentVariable("MIRCLIENT_PERF_BUDGET_MIN_AVG_FPS"),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out double parsedBudgetMinAvgFps) &&
            parsedBudgetMinAvgFps is >= 0 and <= 1000)
        {
            budgetMinAvgFps = parsedBudgetMinAvgFps;
        }

        int budgetMaxDrawCalls = 0;
        if (int.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_PERF_BUDGET_MAX_DRAW_CALLS"), out int parsedBudgetMaxDrawCalls) &&
            parsedBudgetMaxDrawCalls is >= 0 and <= 1_000_000)
        {
            budgetMaxDrawCalls = parsedBudgetMaxDrawCalls;
        }

        int budgetMaxTextureBinds = 0;
        if (int.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_PERF_BUDGET_MAX_TEX_BINDS"), out int parsedBudgetMaxTextureBinds) &&
            parsedBudgetMaxTextureBinds is >= 0 and <= 1_000_000)
        {
            budgetMaxTextureBinds = parsedBudgetMaxTextureBinds;
        }

        int budgetMaxGpuMb = 0;
        if (int.TryParse(Environment.GetEnvironmentVariable("MIRCLIENT_PERF_BUDGET_MAX_GPU_MB"), out int parsedBudgetMaxGpuMb) &&
            parsedBudgetMaxGpuMb is >= 0 and <= 8192)
        {
            budgetMaxGpuMb = parsedBudgetMaxGpuMb;
        }

        path = path.Trim();
        try
        {
            string resolved = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
            path = Path.GetFullPath(resolved);
        }
        catch
        {
        }

        var options = new MirPerfCsvLoggerOptions(
            path,
            intervalMs,
            hitchMs,
            budgetMaxFrameMs,
            budgetMaxCpuMs,
            budgetMinAvgFps,
            budgetMaxDrawCalls,
            budgetMaxTextureBinds,
            budgetMaxGpuMb);
        try
        {
            var logger = new MirPerfCsvLogger(options, log);
            log?.Invoke($"[perf] log enabled: {options.Path} interval={options.IntervalMs}ms hitch={options.HitchThresholdMs}ms");
            return logger;
        }
        catch (Exception ex)
        {
            log?.Invoke($"[perf] log init failed: {ex.GetType().Name}: {ex.Message}");
            return null;
        }
    }

    public void Tick(long nowMs, in MirPerfCsvSample sample)
    {
        if (_disabled)
            return;

        int hitchThresholdMs = Options.HitchThresholdMs;
        _maxFrameMs = Math.Max(_maxFrameMs, sample.LastFrameMs);
        BudgetViolation violations = EvaluateBudget(sample);

        bool isHitch = hitchThresholdMs > 0 && sample.LastFrameMs >= hitchThresholdMs;
        if (isHitch)
        {
            if (!_hitchActive)
            {
                _hitchActive = true;
                _hitchCount++;
                WriteRow(nowMs, "hitch", sample, violations);
            }
        }
        else
        {
            _hitchActive = false;
        }

        if (violations != BudgetViolation.None)
        {
            if (!_budgetActive || _budgetViolations != violations)
            {
                _budgetActive = true;
                _budgetViolations = violations;
                WriteRow(nowMs, "budget", sample, violations);
            }
        }
        else
        {
            _budgetActive = false;
            _budgetViolations = BudgetViolation.None;
        }

        int intervalMs = Options.IntervalMs;
        if (intervalMs <= 0)
            return;

        if (_nextWriteMs == 0 || nowMs >= _nextWriteMs)
        {
            _nextWriteMs = nowMs + intervalMs;
            WriteRow(nowMs, "sample", sample, violations);
        }
    }

    public void Dispose() => CloseWriter();

    private void WriteRow(long nowMs, string eventName, in MirPerfCsvSample sample, BudgetViolation budgetViolations)
    {
        StreamWriter? writer = EnsureWriter();
        if (writer == null)
            return;

        long gcBytes = 0;
        int gc0 = 0;
        int gc1 = 0;
        int gc2 = 0;
        try
        {
            gcBytes = GC.GetTotalMemory(forceFullCollection: false);
            gc0 = GC.CollectionCount(0);
            gc1 = GC.CollectionCount(1);
            gc2 = GC.CollectionCount(2);
        }
        catch
        {
        }

        long wsBytes = 0;
        long privateBytes = 0;
        try
        {
            using Process process = Process.GetCurrentProcess();
            wsBytes = process.WorkingSet64;
            privateBytes = process.PrivateMemorySize64;
        }
        catch
        {
        }

        MirPerfCacheStats? gpuWil = sample.GpuWil;
        MirPerfCacheStats? gpuData = sample.GpuData;

        int? gpuTotalCount = gpuWil.HasValue && gpuData.HasValue ? gpuWil.Value.Count + gpuData.Value.Count : null;
        long? gpuTotalCur = gpuWil.HasValue && gpuData.HasValue ? gpuWil.Value.CurrentBytes + gpuData.Value.CurrentBytes : null;
        long? gpuTotalBudget = gpuWil.HasValue && gpuData.HasValue ? gpuWil.Value.BudgetBytes + gpuData.Value.BudgetBytes : null;

        _lineBuilder.Clear();

        AppendCsvCell(_lineBuilder, DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, nowMs);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, eventName);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.Stage.ToString());
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.MapTitle);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.CenterX);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.CenterY);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.ActorCount);
        _lineBuilder.Append(',');

        AppendCsvCell(_lineBuilder, sample.AvgFps);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.AvgFrameMs);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.AvgCpuMs);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.LastFrameMs);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.LastCpuMs);
        _lineBuilder.Append(',');

        AppendCsvCell(_lineBuilder, sample.DrawCalls);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.TextureBinds);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.Sprites);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.ScissorChanges);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.VSync ? 1 : 0);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.TargetFps);
        _lineBuilder.Append(',');

        AppendCache(_lineBuilder, sample.CpuWil);
        _lineBuilder.Append(',');
        AppendCache(_lineBuilder, sample.CpuData);
        _lineBuilder.Append(',');
        AppendCache(_lineBuilder, gpuWil);
        _lineBuilder.Append(',');
        AppendCache(_lineBuilder, gpuData);
        _lineBuilder.Append(',');

        AppendCsvCell(_lineBuilder, gpuTotalCount);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, gpuTotalCur.HasValue ? gpuTotalCur.Value / (1024 * 1024) : (long?)null);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, gpuTotalBudget.HasValue ? gpuTotalBudget.Value / (1024 * 1024) : (long?)null);
        _lineBuilder.Append(',');

        AppendCsvCell(_lineBuilder, sample.BackBufferWidth);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.BackBufferHeight);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.LogicalWidth);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.LogicalHeight);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.ViewScaleX);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.ViewScaleY);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.ViewOffsetX);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, sample.ViewOffsetY);
        _lineBuilder.Append(',');

        AppendCsvCell(_lineBuilder, gcBytes / (1024 * 1024));
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, gc0);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, gc1);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, gc2);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, wsBytes / (1024 * 1024));
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, privateBytes / (1024 * 1024));
        _lineBuilder.Append(',');

        AppendCsvCell(_lineBuilder, Options.HitchThresholdMs);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, _hitchCount);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, _maxFrameMs);
        _lineBuilder.Append(',');

        AppendCsvCell(_lineBuilder, Options.BudgetMaxFrameMs);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, Options.BudgetMaxCpuMs);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, Options.BudgetMinAvgFps);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, Options.BudgetMaxDrawCalls);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, Options.BudgetMaxTextureBinds);
        _lineBuilder.Append(',');
        AppendCsvCell(_lineBuilder, Options.BudgetMaxGpuMb);
        _lineBuilder.Append(',');
        AppendBudgetReasons(_lineBuilder, budgetViolations);

        try
        {
            writer.WriteLine(_lineBuilder.ToString());
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[perf] log write failed: {ex.GetType().Name}: {ex.Message}");
            _disabled = true;
            CloseWriter();
        }
    }

    private BudgetViolation EvaluateBudget(in MirPerfCsvSample sample)
    {
        var options = Options;
        BudgetViolation violations = BudgetViolation.None;

        if (options.BudgetMinAvgFps > 0 && sample.AvgFps > 0 && sample.AvgFps < options.BudgetMinAvgFps)
            violations |= BudgetViolation.AvgFpsLow;

        if (options.BudgetMaxFrameMs > 0 && sample.LastFrameMs >= options.BudgetMaxFrameMs)
            violations |= BudgetViolation.FrameMsHigh;

        if (options.BudgetMaxCpuMs > 0 && sample.LastCpuMs >= options.BudgetMaxCpuMs)
            violations |= BudgetViolation.CpuMsHigh;

        if (options.BudgetMaxDrawCalls > 0 && sample.DrawCalls > options.BudgetMaxDrawCalls)
            violations |= BudgetViolation.DrawCallsHigh;

        if (options.BudgetMaxTextureBinds > 0 && sample.TextureBinds > options.BudgetMaxTextureBinds)
            violations |= BudgetViolation.TextureBindsHigh;

        if (options.BudgetMaxGpuMb > 0 &&
            sample.GpuWil.HasValue &&
            sample.GpuData.HasValue)
        {
            long gpuTotalBytes = sample.GpuWil.Value.CurrentBytes + sample.GpuData.Value.CurrentBytes;
            long gpuTotalMb = gpuTotalBytes / (1024 * 1024);
            if (gpuTotalMb > options.BudgetMaxGpuMb)
                violations |= BudgetViolation.GpuMbHigh;
        }

        return violations;
    }

    private static void AppendBudgetReasons(StringBuilder builder, BudgetViolation violations)
    {
        if (violations == BudgetViolation.None)
            return;

        bool first = true;

        void Append(string label)
        {
            if (!first)
                builder.Append('|');

            builder.Append(label);
            first = false;
        }

        if ((violations & BudgetViolation.AvgFpsLow) != 0)
            Append("avgFpsLow");

        if ((violations & BudgetViolation.FrameMsHigh) != 0)
            Append("frameMsHigh");

        if ((violations & BudgetViolation.CpuMsHigh) != 0)
            Append("cpuMsHigh");

        if ((violations & BudgetViolation.DrawCallsHigh) != 0)
            Append("drawCallsHigh");

        if ((violations & BudgetViolation.TextureBindsHigh) != 0)
            Append("texBindsHigh");

        if ((violations & BudgetViolation.GpuMbHigh) != 0)
            Append("gpuMbHigh");
    }

    private StreamWriter? EnsureWriter()
    {
        if (_disabled)
            return null;

        if (_writer != null)
            return _writer;

        string path = Options.Path;
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);

            byte[] bom = Encoding.UTF8.GetPreamble();
            bool writeHeader = !File.Exists(path) || new FileInfo(path).Length <= bom.Length;

            FileStream stream;
            if (writeHeader)
            {
                stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
                if (bom.Length > 0)
                    stream.Write(bom, 0, bom.Length);
            }
            else
            {
                stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read);
            }

            _writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };

            if (writeHeader)
                _writer.WriteLine(Header);

            return _writer;
        }
        catch (Exception ex)
        {
            _log?.Invoke($"[perf] log init failed: {ex.GetType().Name}: {ex.Message}");
            _disabled = true;
            CloseWriter();
            return null;
        }
    }

    private void CloseWriter()
    {
        try
        {
            _writer?.Dispose();
        }
        catch
        {
        }

        _writer = null;
    }

    private static void AppendCache(StringBuilder builder, MirPerfCacheStats stats)
    {
        AppendCsvCell(builder, stats.Count);
        builder.Append(',');
        AppendCsvCell(builder, stats.CurrentBytes / (1024 * 1024));
        builder.Append(',');
        AppendCsvCell(builder, stats.BudgetBytes / (1024 * 1024));
        builder.Append(',');
        AppendCsvCell(builder, stats.Hits);
        builder.Append(',');
        AppendCsvCell(builder, stats.Misses);
    }

    private static void AppendCache(StringBuilder builder, MirPerfCacheStats? stats)
    {
        if (!stats.HasValue)
        {
            builder.Append(",,,,");
            return;
        }

        AppendCache(builder, stats.Value);
    }

    private static void AppendCsvCell(StringBuilder builder, string? value)
    {
        if (string.IsNullOrEmpty(value))
            return;

        bool needsQuote = false;
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            if (ch is ',' or '"' or '\r' or '\n')
            {
                needsQuote = true;
                break;
            }
        }

        if (!needsQuote)
        {
            builder.Append(value);
            return;
        }

        builder.Append('"');
        for (int i = 0; i < value.Length; i++)
        {
            char ch = value[i];
            if (ch == '"')
                builder.Append("\"\"");
            else
                builder.Append(ch);
        }
        builder.Append('"');
    }

    private static void AppendCsvCell(StringBuilder builder, int value) =>
        builder.Append(value.ToString(CultureInfo.InvariantCulture));

    private static void AppendCsvCell(StringBuilder builder, int? value)
    {
        if (!value.HasValue)
            return;

        AppendCsvCell(builder, value.Value);
    }

    private static void AppendCsvCell(StringBuilder builder, long value) =>
        builder.Append(value.ToString(CultureInfo.InvariantCulture));

    private static void AppendCsvCell(StringBuilder builder, long? value)
    {
        if (!value.HasValue)
            return;

        AppendCsvCell(builder, value.Value);
    }

    private static void AppendCsvCell(StringBuilder builder, double value)
    {
        if (!double.IsFinite(value))
            return;

        builder.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
    }

    private static void AppendCsvCell(StringBuilder builder, float value)
    {
        if (!float.IsFinite(value))
            return;

        builder.Append(value.ToString("0.###", CultureInfo.InvariantCulture));
    }
}
