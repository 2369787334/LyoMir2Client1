namespace MirClient.Core.Systems;

public sealed class AutoMoveSendSystem
{
    private readonly AutoMoveSystem _autoMove;
    private readonly CommandThrottleSystem _throttle;
    private readonly ActSendSystem _actSendSystem;

    public AutoMoveSendSystem(AutoMoveSystem autoMove, CommandThrottleSystem throttle, ActSendSystem actSendSystem)
    {
        _autoMove = autoMove ?? throw new ArgumentNullException(nameof(autoMove));
        _throttle = throttle ?? throw new ArgumentNullException(nameof(throttle));
        _actSendSystem = actSendSystem ?? throw new ArgumentNullException(nameof(actSendSystem));
    }

    public void Pump(
        MirSessionStage stage,
        bool mapLoaded,
        bool mapCenterSet,
        int curX,
        int curY,
        long nowMs,
        Func<int, int, bool> isWalkable,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(isWalkable);

        if (!_autoMove.Active)
            return;

        if (!mapLoaded || !mapCenterSet)
        {
            _autoMove.Cancel();
            return;
        }

        if (!_autoMove.TryGetSendCandidate(
                stage: stage,
                curX: curX,
                curY: curY,
                nowMs: nowMs,
                isWalkable: isWalkable,
                out AutoMoveSystem.AutoMoveSendCandidate candidate))
        {
            return;
        }

        if (!_throttle.TryMoveSend())
            return;

        _autoMove.CommitSend(candidate.PendingIndex, nowMs);
        _ = _actSendSystem.SendAsync(candidate.Act, candidate.X, candidate.Y, candidate.Dir, token);
    }
}

