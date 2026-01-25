namespace MirClient.Core.Util;

public static class ClFunc
{
    public static int GetFlyDirection16(int sX, int sY, int ttx, int tty)
    {
        double fx = ttx - sX;
        double fy = tty - sY;

        if (fx == 0)
            return fy < 0 ? 0 : 8;

        if (fy == 0)
            return fx < 0 ? 12 : 4;

        int result = 0;

        if (fx > 0 && fy < 0)
        {
            result = 4;
            if (-fy > fx / 4) result = 3;
            if (-fy > fx / 1.9) result = 2;
            if (-fy > fx * 1.4) result = 1;
            if (-fy > fx * 4) result = 0;
        }

        if (fx > 0 && fy > 0)
        {
            result = 4;
            if (fy > fx / 4) result = 5;
            if (fy > fx / 1.9) result = 6;
            if (fy > fx * 1.4) result = 7;
            if (fy > fx * 4) result = 8;
        }

        if (fx < 0 && fy > 0)
        {
            result = 12;
            if (fy > -fx / 4) result = 11;
            if (fy > -fx / 1.9) result = 10;
            if (fy > -fx * 1.4) result = 9;
            if (fy > -fx * 4) result = 8;
        }

        if (fx < 0 && fy < 0)
        {
            result = 12;
            if (-fy > -fx / 4) result = 13;
            if (-fy > -fx / 1.9) result = 14;
            if (-fy > -fx * 1.4) result = 15;
            if (-fy > -fx * 4) result = 0;
        }

        return result;
    }
}
