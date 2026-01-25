using MirClient.Protocol.Text;
using System.Linq;

namespace MirClient.Core.LocalConfig;

public sealed class ItemFilterStore
{
    public readonly record struct ItemFilterRule(int Category, bool Rare, bool Pick, bool Show);
    public readonly record struct LoadResult(bool Loaded, int Count, string FilePath);
    public readonly record struct SaveResult(bool Saved, int Count, string FilePath);

    private readonly Dictionary<string, ItemFilterRule> _defaultRules = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ItemFilterRule> _rules = new(StringComparer.Ordinal);

    public IReadOnlyDictionary<string, ItemFilterRule> Rules => _rules;

    public void ResetToDefaults()
    {
        _rules.Clear();
        foreach (var kv in _defaultRules)
            _rules[kv.Key] = kv.Value;
    }

    public LoadResult LoadDefaultsFromDataDir(string dataDir)
    {
        _defaultRules.Clear();
        _rules.Clear();

        if (string.IsNullOrWhiteSpace(dataDir))
            return new LoadResult(Loaded: false, Count: 0, FilePath: string.Empty);

        string[] candidates =
        [
            Path.Combine(dataDir, "lsDefaultItemFilter.txt"),
            Path.Combine(dataDir, "DefaultItemFilter.dat"),
            Path.Combine(dataDir, "DefaultItemFilter2.dat")
        ];

        string filePath = candidates.FirstOrDefault(File.Exists) ?? string.Empty;
        if (filePath.Length == 0)
            return new LoadResult(Loaded: false, Count: 0, FilePath: string.Empty);

        foreach (string raw in File.ReadLines(filePath, GbkEncoding.Instance))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
                continue;

            if (!TryParseDefaultRuleLine(line, out string name, out ItemFilterRule rule))
                continue;

            _defaultRules[name] = rule;
            _rules[name] = rule;
        }

        return new LoadResult(Loaded: true, Count: _defaultRules.Count, FilePath: filePath);
    }

    public int LoadOverridesFromFile(string filePath)
    {
        if (_defaultRules.Count == 0 || string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return 0;

        int applied = 0;

        foreach (string raw in File.ReadLines(filePath, GbkEncoding.Instance))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';'))
                continue;

            if (!TryParseOverrideRuleLine(line, out string name, out bool rare, out bool pick, out bool show))
                continue;

            if (!_defaultRules.TryGetValue(name, out ItemFilterRule def))
                continue;

            var next = def with { Rare = rare, Pick = pick, Show = show };
            _rules[name] = next;
            applied++;
        }

        return applied;
    }

    public SaveResult SaveOverridesToFile(string filePath)
    {
        if (_defaultRules.Count == 0 || _rules.Count == 0 || string.IsNullOrWhiteSpace(filePath))
            return new SaveResult(Saved: false, Count: 0, FilePath: filePath ?? string.Empty);

        var lines = new List<string>(64);

        foreach (var kv in _rules)
        {
            string name = kv.Key;
            ItemFilterRule cur = kv.Value;

            if (!_defaultRules.TryGetValue(name, out ItemFilterRule def))
                continue;

            if (cur.Rare == def.Rare && cur.Pick == def.Pick && cur.Show == def.Show)
                continue;

            lines.Add($"{name},{(cur.Rare ? 1 : 0)},{(cur.Pick ? 1 : 0)},{(cur.Show ? 1 : 0)}");
        }

        if (lines.Count == 0)
            return new SaveResult(Saved: false, Count: 0, FilePath: filePath);

        string? dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllLines(filePath, lines, GbkEncoding.Instance);
        return new SaveResult(Saved: true, Count: lines.Count, FilePath: filePath);
    }

    private static bool TryParseDefaultRuleLine(string line, out string name, out ItemFilterRule rule)
    {
        name = string.Empty;
        rule = default;

        string[] parts = line.Split(',', StringSplitOptions.None);
        if (parts.Length < 5)
            return false;

        name = parts[0].Trim();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        int category = -1;
        _ = int.TryParse(parts[1].Trim(), out category);

        bool rare = parts[2].Trim() == "1";
        bool pick = parts[3].Trim() == "1";
        bool show = parts[4].Trim() == "1";

        rule = new ItemFilterRule(category, rare, pick, show);
        return true;
    }

    private static bool TryParseOverrideRuleLine(string line, out string name, out bool rare, out bool pick, out bool show)
    {
        name = string.Empty;
        rare = false;
        pick = false;
        show = false;

        string[] parts = line.Split(',', StringSplitOptions.None);
        if (parts.Length < 4)
            return false;

        name = parts[0].Trim();
        if (string.IsNullOrWhiteSpace(name))
            return false;

        rare = parts[1].Trim() == "1";
        pick = parts[2].Trim() == "1";
        show = parts[3].Trim() == "1";
        return true;
    }
}
