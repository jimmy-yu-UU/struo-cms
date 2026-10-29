using AwesomeAssertions;
using Xunit;

namespace Struo.Tests.Template.Documentation;

public sealed class BarePathCitationsTests
{
    private static readonly HashSet<string> Existing = new(StringComparer.Ordinal)
    {
        "AGENTS.md",
        "src/Struo.Api/Program.cs",
        "src/Struo.Api",
        "docs/ai",
        "docs/ai/conventions.md",
        "tests/Struo.Tests.Template/Documentation/CodeCitationConventionTests.cs",
    };

    private static IReadOnlyList<string> Find(string line, bool samplesPresent = false) =>
        BarePathCitations.FindUnresolved(line, path =>
            Existing.Contains(path) || (samplesPresent && path == "samples"));

    [Theory]
    [InlineData("see `tests/Struo.Tests/Template/TemplateInvariantsTests.cs` for it", "tests/Struo.Tests/Template/TemplateInvariantsTests.cs")] // citation-guard:allow: literal fixture, not a real citation
    [InlineData("see src/Struo.Api/Gone.cs.", "src/Struo.Api/Gone.cs")] // citation-guard:allow: literal fixture, not a real citation
    [InlineData("in `docs/ai/missing/`, then", "docs/ai/missing/")] // citation-guard:allow: literal fixture, not a real citation
    [InlineData("(docs/ai/nope.md)", "docs/ai/nope.md")] // citation-guard:allow: literal fixture, not a real citation
    [InlineData("per frontend/src/stores/gone.ts", "frontend/src/stores/gone.ts")] // citation-guard:allow: literal fixture, not a real citation
    [InlineData("the `src/Struo.Gone` project", "src/Struo.Gone")] // citation-guard:allow: literal fixture, not a real citation
    [InlineData("under `tests/Struo.Tests/Support`", "tests/Struo.Tests/Support")] // citation-guard:allow: literal fixture, not a real citation
    [InlineData("in src/Struo.Api/Gone.cs, then", "src/Struo.Api/Gone.cs")] // citation-guard:allow: literal fixture, not a real citation
    [InlineData("in src/Struo.Api/Gone.cs; then", "src/Struo.Api/Gone.cs")] // citation-guard:allow: literal fixture, not a real citation
    public void A_stale_bare_path_is_reported(string line, string expected) =>
        Find(line).Should().Equal(expected);

    [Theory]
    [InlineData("see `src/Struo.Api/Program.cs`")]
    [InlineData("see `docs/ai/conventions.md`, and AGENTS.md.")]
    [InlineData("the `docs/ai/` directory and docs/ai")]
    [InlineData("the `src/Struo.Api` project")]
    public void An_existing_path_is_not_reported(string line) =>
        Find(line).Should().BeEmpty();

    [Theory]
    [InlineData("src/Struo.Api/Gone.cs:12")] // citation-guard:allow: literal fixture, not a real citation
    [InlineData("src/Struo.Api/Gone.cs#L12")] // citation-guard:allow: literal fixture, not a real citation
    [InlineData("src/Struo.Api/Gone.cs：12")] // citation-guard:allow: literal fixture, not a real citation
    [InlineData("src/Struo.Api/Gone.cs: 12")] // citation-guard:allow: literal fixture, not a real citation
    public void The_line_form_is_left_to_the_line_number_check(string line) =>
        Find(line).Should().BeEmpty();

    [Theory]
    [InlineData("src/Struo.*/Gone.cs")]
    [InlineData("src/<name>/Gone.cs")]
    [InlineData("src/{Module}/Gone.cs")]
    [InlineData("src/MyFork.Api/Gone.cs")]
    [InlineData("samples/Acme.Content/Gone.cs")]
    [InlineData("src/MyApp/Gone.cs")]
    [InlineData("docs/_archive-local/superpowers/specs/gone.md")]
    [InlineData("MyFork/src/Struo.Api/Gone.cs")]
    [InlineData("../src/Gone.cs")]
    [InlineData("a/docs/ai/gone.md")]
    [InlineData("https://github.com/o/r/blob/main/src/Gone.cs")]
    public void Example_placeholder_glob_upstream_and_out_of_repo_paths_are_not_reported(string line) =>
        Find(line, samplesPresent: true).Should().BeEmpty();

    [Fact]
    public void An_example_name_matches_whole_segments_only() =>
        Find("src/MyForkish/Gone.cs").Should().Equal("src/MyForkish/Gone.cs"); // citation-guard:allow: literal fixture, not a real citation

    [Fact]
    public void A_samples_path_is_checked_while_the_samples_directory_exists() =>
        Find("samples/Blog/Gone.cs", samplesPresent: true).Should().Equal("samples/Blog/Gone.cs"); // citation-guard:allow: literal fixture, not a real citation

    [Fact]
    public void A_samples_path_is_skipped_once_the_samples_directory_is_gone() =>
        Find("samples/Blog/Gone.cs", samplesPresent: false).Should().BeEmpty(); // citation-guard:allow: literal fixture, not a real citation

    [Fact]
    public void Every_stale_path_on_a_line_is_reported_in_order() =>
        Find("`src/A/Gone.cs` and `docs/B/Gone.md`").Should().Equal("src/A/Gone.cs", "docs/B/Gone.md"); // citation-guard:allow: literal fixture, not a real citation

    [Theory]
    [InlineData("docs/guide/en/changelog.md", true)]
    [InlineData("docs/guide/zh-TW/changelog.md", true)]
    [InlineData("docs/guide/en/22-testing.md", false)]
    [InlineData("docs/ai/changelog.md", false)] // citation-guard:allow: literal fixture, not a real citation
    public void The_locale_changelogs_are_the_files_the_guard_exempts(string path, bool exempt) =>
        NarrativeScanScope.IsLocaleChangelog(path).Should().Be(exempt);

    [Fact]
    public void A_path_counts_as_existing_when_its_example_twin_exists()
    {
        var root = Directory.CreateTempSubdirectory("bare-path-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "local.json.example"), "{}");
            File.WriteAllText(Path.Combine(root, "tracked.json"), "{}");

            BarePathCitations.Exists(root, "local.json").Should().BeTrue();
            BarePathCitations.Exists(root, "tracked.json").Should().BeTrue();
            BarePathCitations.Exists(root, "missing.json").Should().BeFalse();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
