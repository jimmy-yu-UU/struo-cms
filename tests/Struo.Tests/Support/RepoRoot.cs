namespace Struo.Tests.Support;

/// <summary>
/// Locates the repository root by walking up from the test binary's directory
/// (bin/&lt;config&gt;/net10.0) until it finds the directory holding StruoCMS.slnx.
/// Throws rather than falling back to a guess: a wrong root would make the schema-snapshot
/// gate compare against — or overwrite — the wrong file, which is exactly the kind of silent
/// failure that gate exists to prevent.
/// </summary>
internal static class RepoRoot
{
    private const string SolutionFileName = "StruoCMS.slnx";

    public static string Find() => Find(AppContext.BaseDirectory);

    /// <summary>
    /// Overload seam for testing the fail-loud behavior: lets a test point the walk at a
    /// directory with no .slnx ancestor without needing to relocate the real test binary.
    /// </summary>
    internal static string Find(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, SolutionFileName)))
                return dir.FullName;
        }

        throw new InvalidOperationException(
            $"Could not locate the repository root: no '{SolutionFileName}' in any ancestor of " +
            $"'{startDirectory}'.");
    }

    public static string SchemaSnapshotPath() =>
        Path.Combine(Find(), "schema", "core-collections.json");
}
