using MirClient.Core.World;
using MirClient.Protocol;

namespace MirClient.Core.Systems;

public sealed class AutoMoveSystem
{
    private const long PendingTimeoutMs = 1500;
    private const int DefaultMaxVisitedNodes = 120_000;
    private const int ResolveTargetMaxRadius = 4;

    private List<(int X, int Y)>? _path;
    private int _pathIndex;
    private int _pendingIndex = -1;
    private long _pendingSinceMs;
    private bool _wantsRun;

    public bool Active => _path is { Count: > 1 };

    public void Reset() => Cancel();

    public void Cancel()
    {
        _path = null;
        _pathIndex = 0;
        _pendingIndex = -1;
        _pendingSinceMs = 0;
        _wantsRun = false;
    }

    public AutoMoveStartResult TryStart(
        int startX,
        int startY,
        int requestedX,
        int requestedY,
        int mapWidth,
        int mapHeight,
        Func<int, int, bool> isWalkable,
        bool wantsRun,
        int maxVisitedNodes = DefaultMaxVisitedNodes)
    {
        Cancel();

        if (!TryResolveWalkableMoveTarget(
                requestedX,
                requestedY,
                mapWidth,
                mapHeight,
                isWalkable,
                out int targetX,
                out int targetY))
        {
            return new AutoMoveStartResult(AutoMoveStartStatus.NotStarted, TargetX: 0, TargetY: 0, Steps: 0);
        }

        if (startX == targetX && startY == targetY)
            return new AutoMoveStartResult(AutoMoveStartStatus.NotStarted, targetX, targetY, Steps: 0);

        if (!MirPathFinder.TryFindPath(
                startX,
                startY,
                targetX,
                targetY,
                mapWidth,
                mapHeight,
                isWalkable,
                maxVisitedNodes,
                out List<(int X, int Y)> path))
        {
            return new AutoMoveStartResult(AutoMoveStartStatus.PathNotFound, targetX, targetY, Steps: 0);
        }

        if (path.Count <= 1)
            return new AutoMoveStartResult(AutoMoveStartStatus.NotStarted, targetX, targetY, Steps: 0);

        _path = path;
        _pathIndex = 0;
        _pendingIndex = -1;
        _pendingSinceMs = 0;
        _wantsRun = wantsRun;

        return new AutoMoveStartResult(AutoMoveStartStatus.Started, targetX, targetY, Steps: path.Count - 1);
    }

    public bool TryGetSendCandidate(
        MirSessionStage stage,
        int curX,
        int curY,
        long nowMs,
        Func<int, int, bool> isWalkable,
        out AutoMoveSendCandidate candidate)
    {
        candidate = default;

        if (_path is not { Count: > 1 } path)
            return false;

        if (stage is not MirSessionStage.RunGate and not MirSessionStage.InGame)
        {
            Cancel();
            return false;
        }

        if ((uint)_pathIndex >= (uint)path.Count)
        {
            Cancel();
            return false;
        }

        if (_pendingIndex >= 0)
        {
            if ((uint)_pendingIndex < (uint)path.Count &&
                path[_pendingIndex].X == curX &&
                path[_pendingIndex].Y == curY)
            {
                _pathIndex = _pendingIndex;
                _pendingIndex = -1;
                _pendingSinceMs = 0;
            }
            else
            {
                if (_pendingSinceMs > 0 && nowMs - _pendingSinceMs > PendingTimeoutMs)
                    Cancel();
                return false;
            }
        }

        if (path[_pathIndex].X != curX || path[_pathIndex].Y != curY)
        {
            Cancel();
            return false;
        }

        if (_pathIndex >= path.Count - 1)
        {
            Cancel();
            return false;
        }

        int nextIndex = _pathIndex + 1;
        (int nextX, int nextY) = path[nextIndex];
        if (!MirDirection.TryGetDirForStep(curX, curY, nextX, nextY, out byte dir))
        {
            Cancel();
            return false;
        }

        ushort act = Grobal2.CM_WALK;
        int destX = nextX;
        int destY = nextY;
        int pendingIndex = nextIndex;

        if (_wantsRun && nextIndex + 1 < path.Count)
        {
            (int runX, int runY) = path[nextIndex + 1];
            if (MirDirection.TryGetDirForStep(nextX, nextY, runX, runY, out byte runDir) && runDir == dir)
            {
                act = Grobal2.CM_RUN;
                destX = runX;
                destY = runY;
                pendingIndex = nextIndex + 1;
            }
        }

        if (act == Grobal2.CM_RUN)
        {
            if (!isWalkable(nextX, nextY) || !isWalkable(destX, destY))
            {
                Cancel();
                return false;
            }
        }
        else if (!isWalkable(destX, destY))
        {
            Cancel();
            return false;
        }

        candidate = new AutoMoveSendCandidate(act, destX, destY, dir, pendingIndex);
        return true;
    }

    public void CommitSend(int pendingIndex, long nowMs)
    {
        if (_path == null)
            return;

        _pendingIndex = pendingIndex;
        _pendingSinceMs = nowMs;
    }

    private static bool TryResolveWalkableMoveTarget(
        int requestedX,
        int requestedY,
        int mapWidth,
        int mapHeight,
        Func<int, int, bool> isWalkable,
        out int targetX,
        out int targetY)
    {
        targetX = requestedX;
        targetY = requestedY;

        if (mapWidth <= 0 || mapHeight <= 0)
            return false;

        if ((uint)requestedX >= (uint)mapWidth || (uint)requestedY >= (uint)mapHeight)
            return false;

        if (isWalkable(requestedX, requestedY))
            return true;

        for (int r = 1; r <= ResolveTargetMaxRadius; r++)
        {
            int x0 = Math.Max(0, requestedX - r);
            int x1 = Math.Min(mapWidth - 1, requestedX + r);
            int y0 = Math.Max(0, requestedY - r);
            int y1 = Math.Min(mapHeight - 1, requestedY + r);

            for (int y = y0; y <= y1; y++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    if (Math.Abs(x - requestedX) != r && Math.Abs(y - requestedY) != r)
                        continue;

                    if (!isWalkable(x, y))
                        continue;

                    targetX = x;
                    targetY = y;
                    return true;
                }
            }
        }

        return false;
    }

    public enum AutoMoveStartStatus
    {
        NotStarted,
        Started,
        PathNotFound
    }

    public readonly record struct AutoMoveStartResult(AutoMoveStartStatus Status, int TargetX, int TargetY, int Steps);

    public readonly record struct AutoMoveSendCandidate(ushort Act, int X, int Y, byte Dir, int PendingIndex);
}

