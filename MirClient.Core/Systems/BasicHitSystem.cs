using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class BasicHitSystem
{
    private readonly CommandThrottleSystem _throttle;
    private readonly AutoMoveSystem _autoMoveSystem;
    private readonly ActSendSystem _actSendSystem;
    private readonly Action<string>? _log;

    public BasicHitSystem(
        CommandThrottleSystem throttle,
        AutoMoveSystem autoMoveSystem,
        ActSendSystem actSendSystem,
        Action<string>? log = null)
    {
        _throttle = throttle ?? throw new ArgumentNullException(nameof(throttle));
        _autoMoveSystem = autoMoveSystem ?? throw new ArgumentNullException(nameof(autoMoveSystem));
        _actSendSystem = actSendSystem ?? throw new ArgumentNullException(nameof(actSendSystem));
        _log = log;
    }

    public bool TrySend(int myX, int myY, byte dir, int targetId, CancellationToken token)
    {
        if (!_throttle.TryCombatSend())
            return false;

        _autoMoveSystem.Cancel();
        _ = _actSendSystem.SendAsync(Grobal2.CM_HIT, myX, myY, dir, token);
        _log?.Invoke($"[hit] CM_HIT x={myX} y={myY} dir={dir} target={targetId}");
        return true;
    }

    public bool TrySendBasicHit(
        MirWorldState world,
        TargetingSystem targeting,
        int? mouseMapX,
        int? mouseMapY,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(targeting);

        if (!world.MapCenterSet)
            return false;

        int myX = world.MapCenterX;
        int myY = world.MapCenterY;

        int aimX = myX;
        int aimY = myY;
        int aimId = 0;

        if (targeting.TryGetSelectedTarget(world, out int targetId, out ActorMarker target))
        {
            aimId = targetId;
            aimX = target.X;
            aimY = target.Y;
        }
        else if (mouseMapX is int mx && mouseMapY is int my)
        {
            aimX = mx;
            aimY = my;
        }

        byte dir;
        if (aimX != myX || aimY != myY)
        {
            dir = MirDirection.GetFlyDirection(myX, myY, aimX, aimY);
        }
        else if (world.TryGetMyself(out ActorMarker myself))
        {
            dir = (byte)(myself.Dir & 7);
        }
        else
        {
            return false;
        }

        return TrySend(myX, myY, dir, aimId, token);
    }
}
