using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class InventoryQuerySystem
{
    private readonly MirClientSession _session;
    private readonly Action<string>? _log;

    public InventoryQuerySystem(MirClientSession session, Action<string>? log = null)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _log = log;
    }

    public void TryQueryBagItems(int recogMode, string logPrefix, CancellationToken token)
    {
        if (_session.Stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
            return;

        _ = _session.SendClientMessageAsync(Grobal2.CM_QUERYBAGITEMS, recogMode, 0, 0, 0, token)
            .ContinueWith(
                t => _log?.Invoke($"{logPrefix} CM_QUERYBAGITEMS send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke($"{logPrefix} CM_QUERYBAGITEMS");
    }

    public void TryQueryHeroBagItems(string logPrefix, CancellationToken token)
    {
        if (_session.Stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
            return;

        _ = _session.SendClientMessageAsync(Grobal2.CM_QUERYHEROBAGITEMS, 0, 0, 0, 0, token)
            .ContinueWith(
                t => _log?.Invoke($"{logPrefix} CM_QUERYHEROBAGITEMS send failed: {t.Exception?.GetBaseException().Message}"),
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted,
                TaskScheduler.Default);

        _log?.Invoke($"{logPrefix} CM_QUERYHEROBAGITEMS");
    }
}

