using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class MiniMapRequestSystem
{
    private readonly MirClientSession _session;
    private readonly Action<string>? _log;

    public MiniMapRequestSystem(MirClientSession session, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _log = log;
    }

    public void TryRequest(CancellationToken token)
    {
        if (_session.Stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
            return;

        _ = _session.SendClientMessageAsync(Grobal2.CM_WANTMINIMAP, 0, 0, 0, 0, token)
            .ContinueWith(
                t => _log?.Invoke($"[minimap] CM_WANTMINIMAP send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke("[minimap] CM_WANTMINIMAP");
    }
}

