using System.Text.RegularExpressions;

namespace Struo.Tests.Template.Documentation;

/// <summary>
/// Finds bare repo-relative path citations (a path with no <c>:line</c> suffix) that name no existing file
/// or directory. The resolution roots are the ones <see cref="CodeCitationConventionTests"/> resolves
/// <c>file:line</c> citations against, so both checks agree on what is "this repository". A token is
/// only a candidate when it starts at one of those roots (<c>src/</c>, <c>tests/</c>, <c>frontend/src/</c>,
/// <c>schema/</c>, <c>docs/</c>, <c>samples/</c>, or the root files <c>AGENTS.md</c>/<c>CLAUDE.md</c>) and
/// is either extension-anchored or ends in a slash; everything else is treated as upstream or prose, just
/// as an unresolvable basename is on the <c>:line</c> side. Globs, placeholders, fork example paths and
/// anything under <c>docs/_archive-local</c> are skipped.
/// </summary>
internal static partial class BarePathCitations
{
    private const string ArchivePrefix = "docs/_archive-local";
    private static readonly string[] ExampleMarkers = ["MyFork", "MyApp", "MyProject"];

    // Candidate token: starts at a path boundary, runs over path characters. Trailing punctuation is
    // trimmed afterwards. A following ":<digits>" / "#L<digits>" means the :line check owns the cite.
    [GeneratedRegex(@"(?<![A-Za-z0-9_./\\{}<>*~-])(?<path>(?:src|tests|frontend/src|schema|docs|samples)/[A-Za-z0-9_./*<>{}~-]*|(?:AGENTS|CLAUDE)\.md)(?<next>[:：]\s?L?\d|#L\d)?")]
    private static partial Regex Candidate();

    /// <summary>
    /// Returns every candidate path on <paramref name="line"/> for which <paramref name="exists"/> (given a
    /// forward-slash repo-relative path) is false, in order of appearance.
    /// </summary>
    public static IReadOnlyList<string> FindUnresolved(string line, Func<string, bool> exists)
    {
        var unresolved = new List<string>();
        foreach (Match match in Candidate().Matches(line))
        {
            if (match.Groups["next"].Success)
                continue;

            var path = match.Groups["path"].Value.TrimEnd('.', ',', ';', ')');
            if (!IsCheckable(path))
                continue;
            if (!exists(path.TrimEnd('/')))
                unresolved.Add(path);
        }
        return unresolved;
    }

    private static bool IsCheckable(string path)
    {
        if (path.IndexOfAny(['*', '<', '>', '{', '}', '~']) >= 0)
            return false;
        if (ExampleMarkers.Any(marker => path.Contains(marker, StringComparison.Ordinal)))
            return false;
        if (path.StartsWith(ArchivePrefix, StringComparison.Ordinal))
            return false;
        if (path.EndsWith('/'))
            return true;

        var dot = path.LastIndexOf('.');
        return dot > path.LastIndexOf('/') &&
               CodeCitationConventionTests.IsKnownFileExtension(path[(dot + 1)..]);
    }
}
