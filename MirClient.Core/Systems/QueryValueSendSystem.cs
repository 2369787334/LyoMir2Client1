using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class QueryValueSendSystem
{
    private readonly MirClientSession _session;
    private readonly Action<string>? _log;

    public QueryValueSendSystem(MirClientSession session, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _log = log;
    }

    public void TrySend(int merchantId, string value, CancellationToken token)
    {
        if (_session.Stage != MirSessionStage.InGame)
            return;

        _ = _session.SendClientStringAsync(Grobal2.CM_QUERYVAL, merchantId, 0, 0, 0, value, token)
            .ContinueWith(
                t => _log?.Invoke($"[net] CM_QUERYVAL failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke($"[queryval] CM_QUERYVAL sent merchant={merchantId} len={value.Length}");
    }
}

