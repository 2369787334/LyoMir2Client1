using System.IO;

namespace MirClient.Core.Resources;

public readonly record struct MirFileCandidates(string? First, string? Second, string? Third, string? Fourth);

public static class MirFilePathResolver
{
    public static MirFileCandidates GetCandidates(string baseDir, string resourceRoot, string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return default;

        if (string.IsNullOrWhiteSpace(baseDir))
            throw new ArgumentException("Base directory is required.", nameof(baseDir));

        if (string.IsNullOrWhiteSpace(resourceRoot))
            throw new ArgumentException("Resource root is required.", nameof(resourceRoot));

        baseDir = Path.GetFullPath(baseDir);
        resourceRoot = Path.GetFullPath(resourceRoot);

        string input = fileName.Replace('/', '\\').Trim();

        bool resourceRelative = input.StartsWith('$');
        if (resourceRelative)
            input = input[1..];

        while (input.StartsWith(".\\", StringComparison.Ordinal))
            input = input[2..];

        while (input.StartsWith("\\", StringComparison.Ordinal))
            input = input[1..];

        while (input.Contains("\\\\", StringComparison.Ordinal))
            input = input.Replace("\\\\", "\\", StringComparison.Ordinal);

        if (string.IsNullOrWhiteSpace(input))
            return default;

        if (Path.IsPathRooted(input))
        {
            string full = Path.GetFullPath(input);
            return new MirFileCandidates(full, null, null, null);
        }

        if (resourceRelative)
        {
            string full = Path.GetFullPath(Path.Combine(resourceRoot, input));
            return new MirFileCandidates(full, null, null, null);
        }

        string p0 = Path.GetFullPath(Path.Combine(baseDir, input));
        string p1 = Path.GetFullPath(Path.Combine(baseDir, "Data", input));
        string p2 = Path.GetFullPath(Path.Combine(resourceRoot, "Data", input));
        string p3 = Path.GetFullPath(Path.Combine(resourceRoot, input));
        return new MirFileCandidates(p0, p1, p2, p3);
    }
}

