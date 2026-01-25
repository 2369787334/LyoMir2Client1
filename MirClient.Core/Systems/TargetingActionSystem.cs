using MirClient.Core.World;

namespace MirClient.Core.Systems;

public sealed class TargetingActionSystem
{
    private readonly TargetingSystem _targeting;
    private readonly AutoMoveSystem _autoMoveSystem;
    private readonly Action<string>? _log;

    public TargetingActionSystem(TargetingSystem targeting, AutoMoveSystem autoMoveSystem, Action<string>? log = null)
    {
        _targeting = targeting ?? throw new ArgumentNullException(nameof(targeting));
        _autoMoveSystem = autoMoveSystem ?? throw new ArgumentNullException(nameof(autoMoveSystem));
        _log = log;
    }

    public bool TrySelectAt(MirWorldState world, int mapX, int mapY, long nowMs)
    {
        ArgumentNullException.ThrowIfNull(world);

        TargetingSystem.TargetSelectResult result = _targeting.TrySelectAt(world, mapX, mapY, nowMs);
        if (!result.Handled)
            return false;

        _autoMoveSystem.Cancel();

        string name = string.IsNullOrWhiteSpace(result.Actor.UserName) ? "(no name)" : result.Actor.UserName;
        _log?.Invoke($"[target] selected recog={result.SelectedRecogId} x={result.Actor.X} y={result.Actor.Y} name='{name}' feature={result.Actor.Feature}");
        return true;
    }

    public bool TryCycleTargetNearby(MirWorldState world, bool reverse)
    {
        ArgumentNullException.ThrowIfNull(world);

        TargetingSystem.TargetCycleResult result = _targeting.TryCycleTargetNearby(world, reverse);
        if (!result.Handled)
            return false;

        string name = string.IsNullOrWhiteSpace(result.Actor.UserName) ? "(no name)" : result.Actor.UserName;
        _log?.Invoke($"[target] cycle recog={result.SelectedRecogId} x={result.Actor.X} y={result.Actor.Y} name='{name}'");
        return true;
    }
}

