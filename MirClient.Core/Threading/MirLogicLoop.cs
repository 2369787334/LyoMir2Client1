using System.Diagnostics;

namespace MirClient.Core.Threading;




public sealed class MirLogicLoop : IDisposable
{
    private readonly Func<long, long, bool> _step;
    private readonly int _targetFps;
    private Thread? _thread;
    private CancellationTokenSource? _cts;

    public MirLogicLoop(Func<long, long, bool> step, int targetFps)
    {
        _step = step ?? throw new ArgumentNullException(nameof(step));
        _targetFps = targetFps > 0 ? targetFps : 60;
    }

    public bool IsRunning => _thread != null;

    public void Start()
    {
        if (_thread != null)
            return;

        _cts = new CancellationTokenSource();
        _thread = new Thread(() => RunLoop(_cts.Token))
        {
            IsBackground = true,
            Name = "MirLogicLoop"
        };
        _thread.Start();
    }

    public void Stop()
    {
        CancellationTokenSource? cts = _cts;
        Thread? thread = _thread;
        _cts = null;
        _thread = null;

        if (cts == null || thread == null)
            return;

        try { cts.Cancel(); } catch {  }
        try { thread.Join(TimeSpan.FromSeconds(1)); } catch {  }
        cts.Dispose();
    }

    private void RunLoop(CancellationToken cancellationToken)
    {
        long frameTicks = Stopwatch.Frequency / _targetFps;

        while (!cancellationToken.IsCancellationRequested)
        {
            long startTs = Stopwatch.GetTimestamp();
            long nowMs = Environment.TickCount64;

            bool keepRunning = true;
            try
            {
                keepRunning = _step(startTs, nowMs);
            }
            catch
            {
                
            }

            if (!keepRunning)
                break;

            if (frameTicks <= 0)
                continue;

            long elapsedTicks = Stopwatch.GetTimestamp() - startTs;
            long sleepTicks = frameTicks - elapsedTicks;
            if (sleepTicks > 0)
            {
                int sleepMs = (int)(sleepTicks * 1000 / Stopwatch.Frequency);
                if (sleepMs > 0)
                    Thread.Sleep(sleepMs);
            }
        }
    }

    public void Dispose()
    {
        Stop();
    }
}
