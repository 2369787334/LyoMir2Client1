namespace MirClient.Core.Systems;

public sealed class AutoReconnectSystem
{
    public enum AutoReconnectActionKind
    {
        None = 0,
        Disconnect = 1,
        Reconnect = 2
    }

    public readonly record struct AutoReconnectAction(AutoReconnectActionKind Kind, string? Host, int Port)
    {
        public static AutoReconnectAction None => new(AutoReconnectActionKind.None, null, 0);
        public static AutoReconnectAction Disconnect => new(AutoReconnectActionKind.Disconnect, null, 0);
        public static AutoReconnectAction Reconnect(string host, int port) => new(AutoReconnectActionKind.Reconnect, host, port);
    }

    private const int AutoReconnectMaxAttempts = 3;
    private const long AutoReconnectBaseDelayMs = 1000;

    private int _attempts;
    private long _nextAttemptMs;
    private readonly Action<string>? _log;

    public AutoReconnectSystem(Action<string>? log = null)
    {
        _log = log;
    }

    public void Reset()
    {
        _attempts = 0;
        _nextAttemptMs = 0;
    }

    public AutoReconnectAction Tick(
        bool reconnectInProgress,
        bool messagePumpEnabled,
        MirSessionStage stage,
        bool isConnected,
        string? lastRunGateHost,
        int lastRunGatePort,
        long nowMs)
    {
        if (reconnectInProgress || !messagePumpEnabled)
            return AutoReconnectAction.None;

        if (stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
            return AutoReconnectAction.None;

        if (isConnected)
        {
            Reset();
            return AutoReconnectAction.None;
        }

        if (nowMs < _nextAttemptMs)
            return AutoReconnectAction.None;

        if (_attempts >= AutoReconnectMaxAttempts)
        {
            _log?.Invoke("[net] connection lost, auto reconnect exhausted.");
            Reset();
            return AutoReconnectAction.Disconnect;
        }

        if (string.IsNullOrWhiteSpace(lastRunGateHost) || lastRunGatePort is <= 0 or > 65535)
        {
            _log?.Invoke("[net] connection lost (no RunGate endpoint).");
            Reset();
            return AutoReconnectAction.Disconnect;
        }

        _attempts++;
        long backoffMs = AutoReconnectBaseDelayMs * _attempts;
        _nextAttemptMs = nowMs + backoffMs;
        _log?.Invoke($"[net] connection lost, auto reconnect {_attempts}/{AutoReconnectMaxAttempts}...");

        return AutoReconnectAction.Reconnect(lastRunGateHost, lastRunGatePort);
    }
}

