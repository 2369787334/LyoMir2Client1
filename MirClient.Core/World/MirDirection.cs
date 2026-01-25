using MirClient.Protocol;

namespace MirClient.Core.World;

public static class MirDirection
{
    public static bool IsMoveAction(ushort ident) =>
        ident is Grobal2.SM_WALK or Grobal2.SM_RUN or Grobal2.SM_HORSERUN or Grobal2.SM_BACKSTEP or Grobal2.SM_RUSH or Grobal2.SM_RUSHEX or Grobal2.SM_RUSHKUNG;

    public static byte GetFlyDirection(int sx, int sy, int tx, int ty)
    {
        double fx = tx - sx;
        double fy = ty - sy;

        if (fx == 0)
            return (byte)(fy < 0 ? Grobal2.DR_UP : Grobal2.DR_DOWN);

        if (fy == 0)
            return (byte)(fx < 0 ? Grobal2.DR_LEFT : Grobal2.DR_RIGHT);

        if (fx > 0 && fy < 0)
        {
            if (-fy > fx * 2.5) return Grobal2.DR_UP;
            if (-fy < fx / 3) return Grobal2.DR_RIGHT;
            return Grobal2.DR_UPRIGHT;
        }

        if (fx > 0 && fy > 0)
        {
            if (fy < fx / 3) return Grobal2.DR_RIGHT;
            if (fy > fx * 2.5) return Grobal2.DR_DOWN;
            return Grobal2.DR_DOWNRIGHT;
        }

        if (fx < 0 && fy > 0)
        {
            if (fy < -fx / 3) return Grobal2.DR_LEFT;
            if (fy > -fx * 2.5) return Grobal2.DR_DOWN;
            return Grobal2.DR_DOWNLEFT;
        }

        
        if (-fy > -fx * 2.5) return Grobal2.DR_UP;
        if (-fy < -fx / 3) return Grobal2.DR_LEFT;
        return Grobal2.DR_UPLEFT;
    }

    public static (int X, int Y) StepByDir(int x, int y, byte dir, int steps)
    {
        for (int i = 0; i < steps; i++)
        {
            (x, y) = dir switch
            {
                Grobal2.DR_UP => (x, y - 1),
                Grobal2.DR_UPRIGHT => (x + 1, y - 1),
                Grobal2.DR_RIGHT => (x + 1, y),
                Grobal2.DR_DOWNRIGHT => (x + 1, y + 1),
                Grobal2.DR_DOWN => (x, y + 1),
                Grobal2.DR_DOWNLEFT => (x - 1, y + 1),
                Grobal2.DR_LEFT => (x - 1, y),
                Grobal2.DR_UPLEFT => (x - 1, y - 1),
                _ => (x, y)
            };
        }

        return (x, y);
    }

    public static bool TryGetDirForStep(int fromX, int fromY, int toX, int toY, out byte dir)
    {
        dir = 0;

        int dx = toX - fromX;
        int dy = toY - fromY;
        if (dx is < -1 or > 1 || dy is < -1 or > 1 || (dx == 0 && dy == 0))
            return false;

        dir = (dx, dy) switch
        {
            (0, -1) => Grobal2.DR_UP,
            (1, -1) => Grobal2.DR_UPRIGHT,
            (1, 0) => Grobal2.DR_RIGHT,
            (1, 1) => Grobal2.DR_DOWNRIGHT,
            (0, 1) => Grobal2.DR_DOWN,
            (-1, 1) => Grobal2.DR_DOWNLEFT,
            (-1, 0) => Grobal2.DR_LEFT,
            (-1, -1) => Grobal2.DR_UPLEFT,
            _ => 0
        };

        return true;
    }
}
