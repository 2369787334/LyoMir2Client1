using System.Runtime.InteropServices;
using System.Text;

namespace MirClient.Core.LocalConfig;

public static class WayPointIni
{
    private const string KeyName = "WayPoint";

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetPrivateProfileString(
        string section,
        string key,
        string defaultValue,
        StringBuilder retVal,
        int size,
        string filePath);

    [DllImport("kernel32", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool WritePrivateProfileString(
        string section,
        string key,
        string? value,
        string filePath);

    public static IReadOnlyList<(int X, int Y)> Load(string filePath, string mapTitle)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(mapTitle))
            return Array.Empty<(int X, int Y)>();

        var sb = new StringBuilder(32 * 1024);
        _ = GetPrivateProfileString(mapTitle, KeyName, string.Empty, sb, sb.Capacity, filePath);
        return Parse(sb.ToString());
    }

    public static void Save(string filePath, string mapTitle, IReadOnlyList<(int X, int Y)>? points)
    {
        if (string.IsNullOrWhiteSpace(filePath) || string.IsNullOrWhiteSpace(mapTitle))
            return;

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        string value = Build(points);
        _ = WritePrivateProfileString(mapTitle, KeyName, value, filePath);
    }

    private static IReadOnlyList<(int X, int Y)> Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return Array.Empty<(int X, int Y)>();

        var list = new List<(int X, int Y)>(32);

        string s = value;
        while (true)
        {
            s = s.TrimStart();
            if (s.Length == 0)
                break;

            int space = s.IndexOf(' ');
            string token = space >= 0 ? s[..space] : s;
            s = space >= 0 ? s[(space + 1)..] : string.Empty;

            if (token.Length == 0)
                continue;

            int comma = token.IndexOf(',');
            if (comma <= 0 || comma >= token.Length - 1)
                continue;

            if (!int.TryParse(token[..comma], out int x))
                continue;
            if (!int.TryParse(token[(comma + 1)..], out int y))
                continue;

            list.Add((x, y));
        }

        return list;
    }

    private static string Build(IReadOnlyList<(int X, int Y)>? points)
    {
        if (points is not { Count: > 0 })
            return string.Empty;

        var sb = new StringBuilder(points.Count * 12);
        for (int i = 0; i < points.Count; i++)
        {
            (int x, int y) = points[i];
            sb.Append(x);
            sb.Append(',');
            sb.Append(y);
            sb.Append(' ');
        }

        return sb.ToString();
    }
}

