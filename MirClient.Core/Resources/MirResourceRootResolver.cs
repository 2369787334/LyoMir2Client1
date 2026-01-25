using System.IO;

namespace MirClient.Core.Resources;

public static class MirResourceRootResolver
{
    public const string ResourceRootEnvVarName = "MIRCLIENT_RESOURCE_ROOT";

    public static string Resolve(string baseDir, string? startupResourceDir)
    {
        string? envRoot = Environment.GetEnvironmentVariable(ResourceRootEnvVarName);
        return Resolve(baseDir, startupResourceDir, envRoot);
    }

    public static string Resolve(string baseDir, string? startupResourceDir, string? envRoot)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
            throw new ArgumentException("Base directory is required.", nameof(baseDir));

        baseDir = Path.GetFullPath(baseDir);

        if (!string.IsNullOrWhiteSpace(envRoot))
        {
            string resolvedEnv = NormalizeDir(Path.GetFullPath(Path.IsPathRooted(envRoot) ? envRoot : Path.Combine(baseDir, envRoot)));
            return NormalizeCandidate(resolvedEnv);
        }

        string fallback = NormalizeDir(Path.GetFullPath(Path.Combine(baseDir, "Resource")));

        if (!string.IsNullOrWhiteSpace(startupResourceDir))
        {
            string resolved = ResolveStartupResourceDir(baseDir, startupResourceDir);
            resolved = NormalizeCandidate(resolved);
            if (Directory.Exists(Path.Combine(resolved, "Data")))
                return resolved;

            if (TryProbeResourceRoot(baseDir, out string probedRoot))
                return probedRoot;

            return resolved;
        }

        if (TryProbeResourceRoot(baseDir, out string probed))
            return probed;

        return fallback;
    }

    private static string NormalizeCandidate(string candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate) &&
            string.Equals(Path.GetFileName(candidate), "Data", StringComparison.OrdinalIgnoreCase))
        {
            string? parent = Path.GetDirectoryName(candidate);
            if (!string.IsNullOrWhiteSpace(parent))
            {
                parent = NormalizeDir(Path.GetFullPath(parent));
                if (Directory.Exists(Path.Combine(parent, "Data")))
                    return parent;
            }
        }

        return candidate;
    }

    public static string ResolveStartupResourceDir(string baseDir, string startupResourceDir)
    {
        if (string.IsNullOrWhiteSpace(baseDir))
            throw new ArgumentException("Base directory is required.", nameof(baseDir));

        baseDir = Path.GetFullPath(baseDir);

        startupResourceDir = startupResourceDir.Replace('/', '\\').Trim();

        while (startupResourceDir.Length > 0 && (startupResourceDir[0] == '.' || startupResourceDir[0] == '\\'))
            startupResourceDir = startupResourceDir[1..];

        if (string.IsNullOrWhiteSpace(startupResourceDir))
            startupResourceDir = "Resource";

        return NormalizeDir(Path.GetFullPath(Path.IsPathRooted(startupResourceDir) ? startupResourceDir : Path.Combine(baseDir, startupResourceDir)));
    }

    public static bool TryProbeResourceRoot(string baseDir, out string resourceRoot, int maxDepth = 8)
    {
        try
        {
            var current = new DirectoryInfo(Path.GetFullPath(baseDir));
            for (int depth = 0; depth < maxDepth && current != null; depth++)
            {
                if (Directory.Exists(Path.Combine(current.FullName, "Data")))
                {
                    resourceRoot = NormalizeDir(Path.GetFullPath(current.FullName));
                    return true;
                }

                string mirClientSource = Path.Combine(current.FullName, "MirClientSource");
                if (Directory.Exists(Path.Combine(mirClientSource, "Data")))
                {
                    resourceRoot = NormalizeDir(Path.GetFullPath(mirClientSource));
                    return true;
                }

                string resource = Path.Combine(current.FullName, "Resource");
                if (Directory.Exists(Path.Combine(resource, "Data")))
                {
                    resourceRoot = NormalizeDir(Path.GetFullPath(resource));
                    return true;
                }

                current = current.Parent;
            }
        }
        catch
        {
            
        }

        resourceRoot = string.Empty;
        return false;
    }

    private static string NormalizeDir(string path) => Path.TrimEndingDirectorySeparator(path);
}
