using System.Text.RegularExpressions;

namespace Struo.Tests.Template.Documentation;

/// <summary>
/// Finds bare repo-relative path citations (a path with no <c>:line</c> suffix) that name no existing file
/// or directory. A token is a candidate when it starts at one of the roots
/// <see cref="CodeCitationConventionTests"/> resolves <c>file:line</c> citations against
/// (<c>ResolutionRootDirs</c> and <c>ResolutionRootFiles</c>); it may or may not carry an extension and
/// must resolve as a file or a directory. A token that starts anywhere else is treated as upstream or
/// prose, as an unresolvable basename is on the <c>:line</c> side. Skipped: globs, placeholders, example
/// paths (a whole segment equal to, or starting with <c>Name.</c>, one of <see cref="ExampleNames"/>),
/// anything under the excluded archive prefix, and anything under <c>samples/</c> while that directory is
/// absent (a fork deletes it).
/// </summary>
internal static class BarePathCitations
{
    private const string SamplesRoot = "samples";
    private static readonly string[] ExampleNames = ["MyFork", "MyApp", "MyProject", "Acme"];

    // Candidate token: starts at a path boundary, runs over path characters. Trailing punctuation is
    // trimmed afterwards. A following ":<digits>" / "#L<digits>" means the :line check owns the cite.
    private static readonly Regex Candidate = new(
        $@"(?<![A-Za-z0-9_./\{{}}<>*~-])(?<path>(?:{string.Join('|', CodeCitationConventionTests.ResolutionRootDirs.Select(Regex.Escape))})/[A-Za-z0-9_./*<>{{}}~-]*|(?:{string.Join('|', CodeCitationConventionTests.ResolutionRootFiles.Select(Regex.Escape))}))(?<next>[:：]\s?L?\d|#L\d)?",
        RegexOptions.Compiled);

    /// <summary>
    /// Returns every candidate path on <paramref name="line"/> for which <paramref name="exists"/> (given a
    /// forward-slash repo-relative path, no trailing slash) is false, in order of appearance.
    /// </summary>
    public static IReadOnlyList<string> FindUnresolved(string line, Func<string, bool> exists)
    {
        var samplesPresent = exists(SamplesRoot);
        var unresolved = new List<string>();
        foreach (Match match in Candidate.Matches(line))
        {
            if (match.Groups["next"].Success)
                continue;

            var path = match.Groups["path"].Value.TrimEnd('.', ',', ';', ')');
            if (!IsCheckable(path, samplesPresent))
                continue;
            if (!exists(path.TrimEnd('/')))
                unresolved.Add(path);
        }
        return unresolved;
    }

    /// <summary>
    /// True when <paramref name="relativePath"/> names a file or directory under <paramref name="repoRoot"/>,
    /// or when <c>&lt;path&gt;.example</c> does: a gitignored local file (such as
    /// <c>appsettings.Development.json</c>) is tracked as its <c>.example</c> and is absent on a clean checkout.
    /// </summary>
    public static bool Exists(string repoRoot, string relativePath)
    {
        var full = Path.Combine(repoRoot, relativePath);
        return File.Exists(full) || Directory.Exists(full) || File.Exists(full + ".example");
    }

    private static bool IsCheckable(string path, bool samplesPresent)
    {
        if (path.IndexOfAny(['*', '<', '>', '{', '}', '~']) >= 0)
            return false;
        if (path.Split('/').Any(IsExampleSegment))
            return false;
        if (path.StartsWith(CodeCitationConventionTests.ExcludedRelativePrefix, StringComparison.Ordinal))
            return false;
        return samplesPresent || !path.StartsWith(SamplesRoot + "/", StringComparison.Ordinal);
    }

    private static bool IsExampleSegment(string segment) =>
        ExampleNames.Any(name =>
            segment.Equals(name, StringComparison.Ordinal) ||
            segment.StartsWith(name + ".", StringComparison.Ordinal));
}
