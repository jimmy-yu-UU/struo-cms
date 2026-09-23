namespace Struo.Tests.Documentation;

/// <summary>
/// Decides which repo-relative paths the "current state only" guard reads. Markdown directly under
/// <c>docs/guide/</c> — the bilingual landing page and the changelog files — is outside the scan: the
/// changelog's subject is the difference between versions. Rule text: <c>docs/ai/conventions.md</c>,
/// "The changelog".
/// </summary>
internal static class NarrativeScanScope
{
    private const string GuideRoot = "docs/guide/";

    public static bool Includes(string relativePath)
    {
        if (relativePath is "AGENTS.md" or "CLAUDE.md")
            return true;

        var ext = Path.GetExtension(relativePath).ToLowerInvariant();
        if (ext == ".md" && relativePath.StartsWith("docs/", StringComparison.Ordinal))
            return !IsDirectlyUnderGuide(relativePath);
        if (ext == ".cs" && (StartsWith(relativePath, "src/") || StartsWith(relativePath, "tests/")))
            return true;
        if ((ext == ".ts" || ext == ".vue") && StartsWith(relativePath, "frontend/"))
            return true;
        return false;
    }

    private static bool IsDirectlyUnderGuide(string relativePath) =>
        StartsWith(relativePath, GuideRoot) && !relativePath[GuideRoot.Length..].Contains('/');

    private static bool StartsWith(string path, string prefix) =>
        path.StartsWith(prefix, StringComparison.Ordinal);
}
