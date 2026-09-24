namespace Struo.Tests.Documentation;

/// <summary>
/// Decides which repo-relative paths the "current state only" guard reads. The two changelog files,
/// <c>docs/guide/&lt;locale&gt;/changelog.md</c>, are outside the scan: their subject is the difference
/// between versions. Rule text: <c>docs/ai/conventions.md</c>, "The changelog".
/// </summary>
internal static class NarrativeScanScope
{
    private const string GuideRoot = "docs/guide/";
    private const string ChangelogFileName = "changelog.md";

    public static bool Includes(string relativePath)
    {
        if (relativePath is "AGENTS.md" or "CLAUDE.md")
            return true;

        var ext = Path.GetExtension(relativePath).ToLowerInvariant();
        if (ext == ".md" && StartsWith(relativePath, "docs/"))
            return !IsLocaleChangelog(relativePath);
        if (ext == ".cs" && (StartsWith(relativePath, "src/") || StartsWith(relativePath, "tests/")))
            return true;
        if ((ext == ".ts" || ext == ".vue") && StartsWith(relativePath, "frontend/"))
            return true;
        return false;
    }

    // docs/guide/<locale>/changelog.md — one locale segment, then the file name, nothing deeper.
    private static bool IsLocaleChangelog(string relativePath)
    {
        if (!StartsWith(relativePath, GuideRoot)) return false;
        var rest = relativePath[GuideRoot.Length..];
        var slash = rest.IndexOf('/');
        return slash > 0 && rest[(slash + 1)..] == ChangelogFileName;
    }

    private static bool StartsWith(string path, string prefix) =>
        path.StartsWith(prefix, StringComparison.Ordinal);
}
