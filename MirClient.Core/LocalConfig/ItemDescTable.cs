using MirClient.Protocol.Text;

namespace MirClient.Core.LocalConfig;

public sealed class ItemDescTable
{
    private readonly Dictionary<string, string> _byName = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, string> ByName => _byName;

    public bool TryGet(string name, out string desc) => _byName.TryGetValue(name, out desc);

    public LoadResult LoadFromFile(string filePath)
    {
        _byName.Clear();

        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return new LoadResult(Loaded: false, Count: 0, FilePath: filePath ?? string.Empty);

        foreach (string raw in File.ReadLines(filePath, GbkEncoding.Instance))
        {
            if (string.IsNullOrWhiteSpace(raw))
                continue;

            int idx = raw.IndexOf('=');
            if (idx <= 0)
                continue;

            string name = raw[..idx].Trim();
            if (string.IsNullOrWhiteSpace(name))
                continue;

            string desc = raw[(idx + 1)..].Trim();
            if (desc.Length == 0)
                continue;

            desc = desc.Replace("\\", string.Empty, StringComparison.Ordinal);
            if (desc.Length == 0)
                continue;

            _byName[name] = desc;
        }

        return new LoadResult(Loaded: true, Count: _byName.Count, FilePath: filePath);
    }

    public readonly record struct LoadResult(bool Loaded, int Count, string FilePath);
}

