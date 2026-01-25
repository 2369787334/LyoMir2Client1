using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class TargetingSystem
{
    private const long ClickCycleWindowMs = 800;
    private const int CycleNearbyMaxDist = 12;

    private int _selectedRecogId;
    private int _selectCycleMapX;
    private int _selectCycleMapY;
    private int _selectCycleIndex;
    private long _selectCycleLastClickMs;

    public int SelectedRecogId => _selectedRecogId;

    public void Reset()
    {
        _selectedRecogId = 0;
        _selectCycleMapX = 0;
        _selectCycleMapY = 0;
        _selectCycleIndex = 0;
        _selectCycleLastClickMs = 0;
    }

    public bool ClearSelected()
    {
        if (_selectedRecogId == 0)
            return false;

        _selectedRecogId = 0;
        return true;
    }

    public bool TryGetSelectedTarget(MirWorldState world, out int recogId, out ActorMarker actor)
    {
        recogId = _selectedRecogId;
        actor = default;

        if (recogId == 0)
            return false;

        if (!world.TryGetActor(recogId, out actor) || actor.IsMyself)
        {
            _selectedRecogId = 0;
            recogId = 0;
            actor = default;
            return false;
        }

        return true;
    }

    public TargetSelectResult TrySelectAt(MirWorldState world, int mapX, int mapY, long nowMs)
    {
        var candidates = new List<(int RecogId, ActorMarker Actor, int Dist)>();
        foreach ((int recogId, ActorMarker actor) in world.Actors)
        {
            if (recogId == 0 || actor.IsMyself)
                continue;

            int dx = actor.X - mapX;
            int dy = actor.Y - mapY;
            if (dx is < -1 or > 1 || dy is < -1 or > 1)
                continue;

            candidates.Add((recogId, actor, Math.Abs(dx) + Math.Abs(dy)));
        }

        if (candidates.Count == 0)
            return default;

        candidates.Sort(static (a, b) =>
        {
            int cmp = a.Dist.CompareTo(b.Dist);
            if (cmp != 0)
                return cmp;
            cmp = b.Actor.Y.CompareTo(a.Actor.Y);
            if (cmp != 0)
                return cmp;
            cmp = b.Actor.X.CompareTo(a.Actor.X);
            if (cmp != 0)
                return cmp;
            return a.RecogId.CompareTo(b.RecogId);
        });

        if (mapX == _selectCycleMapX && mapY == _selectCycleMapY && nowMs - _selectCycleLastClickMs < ClickCycleWindowMs)
        {
            _selectCycleIndex++;
        }
        else
        {
            _selectCycleMapX = mapX;
            _selectCycleMapY = mapY;
            _selectCycleIndex = 0;
        }

        _selectCycleLastClickMs = nowMs;

        _selectCycleIndex %= candidates.Count;

        (int selectedRecogId, ActorMarker selected, _) = candidates[_selectCycleIndex];
        _selectedRecogId = selectedRecogId;
        return new TargetSelectResult(Handled: true, selectedRecogId, selected);
    }

    public TargetCycleResult TryCycleTargetNearby(MirWorldState world, bool reverse)
    {
        if (!world.MapCenterSet)
            return default;

        int myX = world.MapCenterX;
        int myY = world.MapCenterY;

        var candidates = new List<(int RecogId, ActorMarker Actor, int Dist)>();

        foreach ((int recogId, ActorMarker actor) in world.Actors)
        {
            if (recogId == 0 || actor.IsMyself)
                continue;

            if (FeatureCodec.Race(actor.Feature) == Grobal2.RCC_MERCHANT)
                continue;

            int dx = actor.X - myX;
            int dy = actor.Y - myY;
            int dist = Math.Abs(dx) + Math.Abs(dy);
            if (dist > CycleNearbyMaxDist)
                continue;

            candidates.Add((recogId, actor, dist));
        }

        if (candidates.Count == 0)
            return default;

        candidates.Sort(static (a, b) =>
        {
            int cmp = a.Dist.CompareTo(b.Dist);
            if (cmp != 0)
                return cmp;
            cmp = b.Actor.Y.CompareTo(a.Actor.Y);
            if (cmp != 0)
                return cmp;
            cmp = b.Actor.X.CompareTo(a.Actor.X);
            if (cmp != 0)
                return cmp;
            return a.RecogId.CompareTo(b.RecogId);
        });

        int currentIndex = -1;
        if (_selectedRecogId != 0)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                if (candidates[i].RecogId == _selectedRecogId)
                {
                    currentIndex = i;
                    break;
                }
            }
        }

        int nextIndex;
        if (currentIndex < 0)
        {
            nextIndex = 0;
        }
        else
        {
            int delta = reverse ? -1 : 1;
            nextIndex = (currentIndex + delta) % candidates.Count;
            if (nextIndex < 0)
                nextIndex += candidates.Count;
        }

        (int selectedRecogId, ActorMarker selected, _) = candidates[nextIndex];
        _selectedRecogId = selectedRecogId;

        return new TargetCycleResult(Handled: true, selectedRecogId, selected);
    }

    public readonly record struct TargetSelectResult(bool Handled, int SelectedRecogId, ActorMarker Actor);

    public readonly record struct TargetCycleResult(bool Handled, int SelectedRecogId, ActorMarker Actor);
}

