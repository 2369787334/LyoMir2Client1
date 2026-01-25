using System.Diagnostics;

namespace MirClient.Core.Systems;

public sealed class CommandThrottleSystem
{
    private static readonly long MoveCooldownTicks = Stopwatch.Frequency / 8; 
    private static readonly long PickupCooldownTicks = Stopwatch.Frequency / 4; 
    private static readonly long CombatCooldownTicks = Stopwatch.Frequency / 6; 
    private static readonly long LevelRankCooldownTicks = (Stopwatch.Frequency * 300) / 1000; 

    private long _lastMoveCommandTimestamp;
    private long _lastPickupCommandTimestamp;
    private long _lastCombatCommandTimestamp;
    private long _lastLevelRankCommandTimestamp;

    public void Reset()
    {
        _lastMoveCommandTimestamp = 0;
        _lastPickupCommandTimestamp = 0;
        _lastCombatCommandTimestamp = 0;
        _lastLevelRankCommandTimestamp = 0;
    }

    public bool TryMoveSend()
    {
        long now = Stopwatch.GetTimestamp();
        if (now - _lastMoveCommandTimestamp < MoveCooldownTicks)
            return false;

        _lastMoveCommandTimestamp = now;
        return true;
    }

    public bool TryPickupSend()
    {
        long now = Stopwatch.GetTimestamp();
        if (now - _lastPickupCommandTimestamp < PickupCooldownTicks)
            return false;

        _lastPickupCommandTimestamp = now;
        return true;
    }

    public bool TryCombatSend()
    {
        long now = Stopwatch.GetTimestamp();
        if (now - _lastCombatCommandTimestamp < CombatCooldownTicks)
            return false;

        _lastCombatCommandTimestamp = now;
        return true;
    }

    public bool TryLevelRankSend()
    {
        long now = Stopwatch.GetTimestamp();
        if (now - _lastLevelRankCommandTimestamp < LevelRankCooldownTicks)
            return false;

        _lastLevelRankCommandTimestamp = now;
        return true;
    }
}

