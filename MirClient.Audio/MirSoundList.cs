using System.Globalization;

namespace MirClient.Audio;

public sealed class MirSoundList
{
    private readonly Dictionary<int, string> _relativePaths;

    private MirSoundList(Dictionary<int, string> relativePaths)
    {
        _relativePaths = relativePaths;
    }

    public static bool TryLoad(string soundListPath, out MirSoundList list)
    {
        list = null!;
        if (string.IsNullOrWhiteSpace(soundListPath))
            return false;

        soundListPath = Path.GetFullPath(soundListPath);
        if (!File.Exists(soundListPath))
            return false;

        var map = new Dictionary<int, string>();

        foreach (string rawLine in File.ReadLines(soundListPath))
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            if (line.StartsWith(';'))
                continue;

            int colon = line.IndexOf(':');
            if (colon <= 0)
                continue;

            ReadOnlySpan<char> left = line.AsSpan(0, colon).Trim();
            ReadOnlySpan<char> right = line.AsSpan(colon + 1).Trim();
            if (!int.TryParse(left, NumberStyles.Integer, CultureInfo.InvariantCulture, out int id) || id < 0)
                continue;

            if (right.Length == 0)
                continue;

            string rel = right.ToString().Replace('/', '\\').Trim();
            if (rel.Length == 0)
                continue;

            map[id] = rel;
        }

        list = new MirSoundList(map);
        return true;
    }

    public bool TryGetRelativePath(int id, out string relativePath)
    {
        if (_relativePaths.TryGetValue(id, out string? path))
        {
            relativePath = path;
            return true;
        }

        relativePath = string.Empty;
        return false;
    }
}

