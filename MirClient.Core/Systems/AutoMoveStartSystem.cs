using MirClient.Core.World;

namespace MirClient.Core.Systems;

public sealed class AutoMoveStartSystem
{
    private readonly AutoMoveSystem _autoMove;
    private readonly AutoMoveSendSystem _autoMoveSend;
    private readonly Action<string>? _log;

    public AutoMoveStartSystem(AutoMoveSystem autoMove, AutoMoveSendSystem autoMoveSend, Action<string>? log = null)
    {
        _autoMove = autoMove ?? throw new ArgumentNullException(nameof(autoMove));
        _autoMoveSend = autoMoveSend ?? throw new ArgumentNullException(nameof(autoMoveSend));
        _log = log;
    }

    public bool TryStartAutoMove(
        MirSessionStage stage,
        bool mapLoaded,
        MirWorldState world,
        int requestedX,
        int requestedY,
        int mapWidth,
        int mapHeight,
        Func<int, int, bool> isWalkable,
        bool wantsRun,
        long nowMs,
        CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(world);
        ArgumentNullException.ThrowIfNull(isWalkable);

        if (stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
            return false;

        if (!mapLoaded || !world.MapCenterSet)
            return false;

        int startX = world.MapCenterX;
        int startY = world.MapCenterY;

        AutoMoveSystem.AutoMoveStartResult result = _autoMove.TryStart(
            startX,
            startY,
            requestedX: requestedX,
            requestedY: requestedY,
            mapWidth: mapWidth,
            mapHeight: mapHeight,
            isWalkable: isWalkable,
            wantsRun: wantsRun);

        if (result.Status == AutoMoveSystem.AutoMoveStartStatus.PathNotFound)
        {
            _log?.Invoke($"[move] path not found ({startX},{startY} -> {result.TargetX},{result.TargetY})");
            return false;
        }

        if (result.Status != AutoMoveSystem.AutoMoveStartStatus.Started)
            return false;

        _log?.Invoke($"[move] {(wantsRun ? "run" : "walk")} to ({result.TargetX},{result.TargetY}) steps={result.Steps}");

        return true;
    }
}
