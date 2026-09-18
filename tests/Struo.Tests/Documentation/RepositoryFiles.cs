namespace Struo.Tests.Documentation;

/// <summary>
/// Walks a fixed set of root files and root directories under a repository root and returns every
/// included file as a repo-relative, forward-slash path, sorted ordinally. Skips any directory named
/// <c>obj</c>, <c>bin</c>, <c>node_modules</c> or <c>.git</c>, and anything under
/// <c>docs/_archive-local</c>, so a scan never walks generated output or untracked archives.
/// </summary>
internal static class RepositoryFiles
{
    private static readonly string[] ExcludedDirNames = ["obj", "bin", "node_modules", ".git"];
    private const string ExcludedRelativePrefix = "docs/_archive-local";

    public static IReadOnlyList<string> Enumerate(
        string repoRoot,
        IReadOnlyList<string> rootFiles,
        IReadOnlyList<string> rootDirs,
        Func<string, bool> includeRelativePath)
    {
        var results = new List<string>();

        foreach (var relativePath in rootFiles)
        {
            var normalized = relativePath.Replace('\\', '/');
            if (File.Exists(Path.Combine(repoRoot, relativePath)) && includeRelativePath(normalized))
                results.Add(normalized);
        }

        foreach (var rootDir in rootDirs)
        {
            var fullDir = Path.Combine(repoRoot, rootDir);
            if (Directory.Exists(fullDir))
                Walk(new DirectoryInfo(fullDir), repoRoot, includeRelativePath, results);
        }

        results.Sort(StringComparer.Ordinal);
        return results;
    }

    private static void Walk(
        DirectoryInfo dir, string repoRoot, Func<string, bool> includeRelativePath, List<string> results)
    {
        foreach (var file in dir.EnumerateFiles())
        {
            var relativePath = Path.GetRelativePath(repoRoot, file.FullName).Replace('\\', '/');
            if (includeRelativePath(relativePath))
                results.Add(relativePath);
        }

        foreach (var subDir in dir.EnumerateDirectories())
        {
            if (ExcludedDirNames.Contains(subDir.Name, StringComparer.OrdinalIgnoreCase))
                continue;

            var relativeSubDir = Path.GetRelativePath(repoRoot, subDir.FullName).Replace('\\', '/');
            if (relativeSubDir == ExcludedRelativePrefix ||
                relativeSubDir.StartsWith(ExcludedRelativePrefix + "/", StringComparison.Ordinal))
                continue;

            Walk(subDir, repoRoot, includeRelativePath, results);
        }
    }
}
