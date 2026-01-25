using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class ViewRangeSystem
{
    private readonly MirClientSession _session;
    private readonly Action<string>? _log;

    public ViewRangeSystem(MirClientSession session, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _log = log;
    }

    public void TrySend(int viewX, int viewY, string logPrefix, CancellationToken token)
    {
        if (_session.Stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
            return;

        int viewRange = (viewY << 16) | (viewX & 0xFFFF);

        _ = _session.SendClientMessageAsync(Grobal2.CM_WANTVIEWRANGE, viewRange, 0, 0, 0, token)
            .ContinueWith(
                t => _log?.Invoke($"{logPrefix} CM_WANTVIEWRANGE send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke($"{logPrefix} CM_WANTVIEWRANGE viewX={viewX} viewY={viewY}");
    }
}

