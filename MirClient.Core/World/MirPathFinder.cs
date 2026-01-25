using System;
using System.Collections.Generic;

namespace MirClient.Core.World;

public static class MirPathFinder
{
    private readonly record struct NodeInfo(int ParentKey, int G);

    private const int StraightCost = 10;
    private const int DiagonalCost = 14;

    public static bool TryFindPath(
        int startX,
        int startY,
        int targetX,
        int targetY,
        int width,
        int height,
        Func<int, int, bool> isWalkable,
        int maxVisitedNodes,
        out List<(int X, int Y)> path)
    {
        path = [];

        if (width <= 0 || height <= 0)
            return false;

        if ((uint)startX >= (uint)width || (uint)startY >= (uint)height)
            return false;
        if ((uint)targetX >= (uint)width || (uint)targetY >= (uint)height)
            return false;

        if (startX == targetX && startY == targetY)
        {
            path.Add((startX, startY));
            return true;
        }

        if (!isWalkable(targetX, targetY))
            return false;

        int startKey = Encode(startX, startY, width);
        int targetKey = Encode(targetX, targetY, width);

        var open = new PriorityQueue<int, int>();
        open.Enqueue(startKey, priority: Heuristic(startX, startY, targetX, targetY));

        Dictionary<int, NodeInfo> nodes = new(capacity: 2048)
        {
            [startKey] = new NodeInfo(ParentKey: -1, G: 0)
        };

        HashSet<int> closed = new(capacity: 2048);

        int budget = maxVisitedNodes <= 0 ? int.MaxValue : maxVisitedNodes;
        while (open.Count > 0)
        {
            int currentKey = open.Dequeue();
            if (!closed.Add(currentKey))
                continue;

            if (closed.Count > budget)
                return false;

            if (currentKey == targetKey)
            {
                path = ReconstructPath(nodes, currentKey, width);
                return path.Count > 0;
            }

            Decode(currentKey, width, out int cx, out int cy);
            NodeInfo currentInfo = nodes[currentKey];

            foreach ((int dx, int dy) in NeighborDirs)
            {
                int nx = cx + dx;
                int ny = cy + dy;
                if ((uint)nx >= (uint)width || (uint)ny >= (uint)height)
                    continue;

                if (!isWalkable(nx, ny))
                    continue;

                int neighborKey = Encode(nx, ny, width);
                if (closed.Contains(neighborKey))
                    continue;

                int stepCost = (dx == 0 || dy == 0) ? StraightCost : DiagonalCost;
                int tentativeG = currentInfo.G + stepCost;
                if (nodes.TryGetValue(neighborKey, out NodeInfo existing) && tentativeG >= existing.G)
                    continue;

                nodes[neighborKey] = new NodeInfo(currentKey, tentativeG);

                int f = tentativeG + Heuristic(nx, ny, targetX, targetY);
                open.Enqueue(neighborKey, f);
            }
        }

        return false;
    }

    private static int Encode(int x, int y, int width) => (y * width) + x;

    private static void Decode(int key, int width, out int x, out int y)
    {
        y = key / width;
        x = key - (y * width);
    }

    private static int Heuristic(int x, int y, int targetX, int targetY)
    {
        int dx = Math.Abs(targetX - x);
        int dy = Math.Abs(targetY - y);

        int min = Math.Min(dx, dy);
        int max = Math.Max(dx, dy);
        return (DiagonalCost * min) + (StraightCost * (max - min));
    }

    private static List<(int X, int Y)> ReconstructPath(Dictionary<int, NodeInfo> nodes, int endKey, int width)
    {
        if (!nodes.TryGetValue(endKey, out NodeInfo end))
            return [];

        int estimatedSteps = Math.Max(1, (end.G / StraightCost) + 1);
        var path = new List<(int X, int Y)>(capacity: estimatedSteps);

        int currentKey = endKey;
        while (currentKey != -1 && nodes.TryGetValue(currentKey, out NodeInfo info))
        {
            Decode(currentKey, width, out int x, out int y);
            path.Add((x, y));
            currentKey = info.ParentKey;
        }

        path.Reverse();
        return path;
    }

    private static readonly (int dx, int dy)[] NeighborDirs =
    [
        (1, 0),
        (0, -1),
        (-1, 0),
        (0, 1),
        (1, -1),
        (1, 1),
        (-1, 1),
        (-1, -1)
    ];
}
