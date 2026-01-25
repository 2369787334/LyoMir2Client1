using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class KeyboardMoveSystem
{
    private readonly CommandThrottleSystem _throttle;
    private readonly AutoMoveSystem _autoMoveSystem;
    private readonly ActSendSystem _actSendSystem;

    public KeyboardMoveSystem(CommandThrottleSystem throttle, AutoMoveSystem autoMoveSystem, ActSendSystem actSendSystem)
    {
        _throttle = throttle ?? throw new ArgumentNullException(nameof(throttle));
        _autoMoveSystem = autoMoveSystem ?? throw new ArgumentNullException(nameof(autoMoveSystem));
        _actSendSystem = actSendSystem ?? throw new ArgumentNullException(nameof(actSendSystem));
    }

    public bool TrySendArrowMove(
        byte dir,
        bool wantsRun,
        int mapCenterX,
        int mapCenterY,
        bool mapLoaded,
        Func<int, int, bool> isWalkable,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(isWalkable);

        _autoMoveSystem.Cancel();

        int steps = wantsRun ? 2 : 1;
        (int nextX, int nextY) = MirDirection.StepByDir(mapCenterX, mapCenterY, dir, steps);

        bool canMove = true;
        if (mapLoaded)
        {
            if (steps == 1)
            {
                canMove = isWalkable(nextX, nextY);
            }
            else
            {
                (int midX, int midY) = MirDirection.StepByDir(mapCenterX, mapCenterY, dir, 1);
                canMove = isWalkable(midX, midY) && isWalkable(nextX, nextY);
            }
        }

        if (!_throttle.TryMoveSend())
            return true;

        if (!canMove)
        {
            _ = _actSendSystem.SendAsync(Grobal2.CM_TURN, mapCenterX, mapCenterY, dir, token);
            return true;
        }

        ushort act = wantsRun ? Grobal2.CM_RUN : Grobal2.CM_WALK;
        _ = _actSendSystem.SendAsync(act, nextX, nextY, dir, token);
        return true;
    }
}

