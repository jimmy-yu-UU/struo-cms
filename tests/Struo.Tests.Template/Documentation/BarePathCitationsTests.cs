using AwesomeAssertions;
using Xunit;

namespace Struo.Tests.Template.Documentation;

public sealed class BarePathCitationsTests
{
    private static readonly HashSet<string> Existing = new(StringComparer.Ordinal)
    {
        "AGENTS.md",
        "src/Struo.Api/Program.cs",
        "docs/ai",
        "docs/ai/conventions.md",
        "tests/Struo.Tests.Template/Documentation/CodeCitationConventionTests.cs",
    };

    private static IReadOnlyList<string> Find(string line) =>
        BarePathCitations.FindUnresolved(line, Existing.Contains);

    [Theory]
    [InlineData("see `tests/Struo.Tests/Template/TemplateInvariantsTests.cs` for it", "tests/Struo.Tests/Template/TemplateInvariantsTests.cs")] // citation-guard:allow: literal fixture, not a real citation
    [InlineData("see src/Struo.Api/Gone.cs.", "src/Struo.Api/Gone.cs")] // citation-guard:allow: literal fixture, not a real citation
    [InlineData("in `docs/ai/missing/`, then", "docs/ai/missing/")] // citation-guard:allow: literal fixture, not a real citation
    [InlineData("(docs/ai/nope.md)", "docs/ai/nope.md")] // citation-guard:allow: literal fixture, not a real citation
    [InlineData("per frontend/src/stores/gone.ts", "frontend/src/stores/gone.ts")] // citation-guard:allow: literal fixture, not a real citation
    public void A_stale_bare_path_is_reported(string line, string expected) =>
        Find(line).Should().Equal(expected);

    [Theory]
    [InlineData("see `src/Struo.Api/Program.cs`")]
    [InlineData("see `docs/ai/conventions.md`, and AGENTS.md.")]
    [InlineData("the `docs/ai/` directory and docs/ai")]
    public void An_existing_path_is_not_reported(string line) =>
        Find(line).Should().BeEmpty();

    [Theory]
    [InlineData("src/Struo.Api/Gone.cs:12")] // citation-guard:allow: literal regex fixture, not a real citation
    [InlineData("src/Struo.Api/Gone.cs#L12")] // citation-guard:allow: literal regex fixture, not a real citation
    public void The_line_form_is_left_to_the_line_number_check(string line) =>
        Find(line).Should().BeEmpty();

    [Theory]
    [InlineData("src/Struo.*/Gone.cs")]
    [InlineData("src/<name>/Gone.cs")]
    [InlineData("src/{Module}/Gone.cs")]
    [InlineData("src/MyFork.Api/Gone.cs")]
    [InlineData("docs/_archive-local/superpowers/specs/gone.md")]
    [InlineData("MyFork/src/Struo.Api/Gone.cs")]
    [InlineData("../src/Gone.cs")]
    [InlineData("src/Struo.Api/Gone.unknownext")]
    [InlineData("a/docs/ai/gone.md")]
    public void Example_placeholder_glob_upstream_and_out_of_repo_paths_are_not_reported(string line) =>
        Find(line).Should().BeEmpty();

    [Fact]
    public void Every_stale_path_on_a_line_is_reported_in_order() =>
        Find("`src/A/Gone.cs` and `docs/B/Gone.md`").Should().Equal("src/A/Gone.cs", "docs/B/Gone.md"); // citation-guard:allow: literal fixture, not a real citation
}
