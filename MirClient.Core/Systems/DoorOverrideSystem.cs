using MirClient.Core.World;

namespace MirClient.Core.Systems;

public sealed class DoorOverrideSystem
{
    private readonly MirWorldState _world;

    public DoorOverrideSystem(MirWorldState world)
    {
        _world = world ?? throw new ArgumentNullException(nameof(world));
    }

    public bool TryApplyOpenDoorOverride(int x, int y, int mapWidth, int mapHeight, Func<int, int, byte> getDoorIndex)
    {
        return TryApplyDoorOverride(
            x,
            y,
            mapWidth,
            mapHeight,
            getDoorIndex,
            open: true,
            xNegRadius: 10,
            yNegRadius: 10,
            xPosRadius: 10,
            yPosRadius: 10);
    }

    public bool TryApplyCloseDoorOverride(int x, int y, int mapWidth, int mapHeight, Func<int, int, byte> getDoorIndex)
    {
        return TryApplyDoorOverride(
            x,
            y,
            mapWidth,
            mapHeight,
            getDoorIndex,
            open: false,
            xNegRadius: 8,
            yNegRadius: 8,
            xPosRadius: 10,
            yPosRadius: 10);
    }

    private bool TryApplyDoorOverride(
        int x,
        int y,
        int mapWidth,
        int mapHeight,
        Func<int, int, byte> getDoorIndex,
        bool open,
        int xNegRadius,
        int yNegRadius,
        int xPosRadius,
        int yPosRadius)
    {
        ArgumentNullException.ThrowIfNull(getDoorIndex);

        if ((uint)x >= (uint)mapWidth || (uint)y >= (uint)mapHeight)
            return false;

        byte originDoorIndex = getDoorIndex(x, y);
        if ((originDoorIndex & 0x80) == 0)
            return false;

        byte idx = (byte)(originDoorIndex & 0x7F);
        if (idx == 0)
            return false;

        int x0 = Math.Max(0, x - xNegRadius);
        int x1 = Math.Min(mapWidth - 1, x + xPosRadius);
        int y0 = Math.Max(0, y - yNegRadius);
        int y1 = Math.Min(mapHeight - 1, y + yPosRadius);

        for (int ix = x0; ix <= x1; ix++)
        {
            for (int iy = y0; iy <= y1; iy++)
            {
                byte cellDoorIndex = getDoorIndex(ix, iy);
                if ((cellDoorIndex & 0x80) == 0)
                    continue;
                if ((cellDoorIndex & 0x7F) != idx)
                    continue;

                _world.SetDoorOverride(MakeMapKey(ix, iy), open);
            }
        }

        return true;
    }

    private static int MakeMapKey(int x, int y) => (x << 16) | (y & 0xFFFF);
}

