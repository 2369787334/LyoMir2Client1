using System.Globalization;
using MirClient.Protocol.Text;

namespace MirClient.Core.LocalConfig;

public sealed class MapDescTable
{
    private readonly List<MapDescInfo> _entries = new(256);

    public IReadOnlyList<MapDescInfo> Entries => _entries;

    public void Clear() => _entries.Clear();

    public LoadResult LoadFromFile(string filePath)
    {
        _entries.Clear();

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new LoadResult(Loaded: false, Count: 0, FilePath: filePath ?? string.Empty);

        foreach (string raw in File.ReadLines(filePath, GbkEncoding.Instance))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
                continue;

            string[] parts = line.Split(',', StringSplitOptions.None);
            if (parts.Length < 5)
                continue;

            string mapTitle = parts[0].Trim();
            if (string.IsNullOrWhiteSpace(mapTitle))
                continue;

            if (!int.TryParse(parts[1].Trim(), out int x) || x < 0)
                continue;
            if (!int.TryParse(parts[2].Trim(), out int y) || y < 0)
                continue;

            string placeName;
            string colorPart;
            string fullMapPart;

            if (parts.Length >= 6)
            {
                placeName = parts[3].Trim();
                colorPart = parts[4].Trim();
                fullMapPart = parts[5].Trim();
            }
            else
            {
                placeName = string.Empty;
                colorPart = parts[3].Trim();
                fullMapPart = parts[4].Trim();
            }

            if (!TryParseColor(colorPart, out int color))
                continue;
            if (!int.TryParse(fullMapPart, out int fullMap) || fullMap < 0)
                continue;

            _entries.Add(new MapDescInfo(mapTitle, x, y, placeName, color, fullMap));
        }

        return new LoadResult(Loaded: true, Count: _entries.Count, FilePath: filePath);
    }

    private static bool TryParseColor(string value, out int color)
    {
        color = 0;

        if (string.IsNullOrWhiteSpace(value))
            return false;

        string s = value.Trim();

        if (s.StartsWith('$'))
            return int.TryParse(s[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color);

        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(s[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out color);

        return int.TryParse(s, out color);
    }

    public readonly record struct MapDescInfo(string MapTitle, int X, int Y, string PlaceName, int Color, int FullMap);
    public readonly record struct LoadResult(bool Loaded, int Count, string FilePath);
}

