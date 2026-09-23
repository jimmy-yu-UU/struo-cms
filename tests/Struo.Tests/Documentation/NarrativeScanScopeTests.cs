using AwesomeAssertions;

namespace Struo.Tests.Documentation;

public sealed class NarrativeScanScopeTests
{
    [Theory]
    [InlineData("docs/guide/index.md", false)]
    [InlineData("docs/guide/changelog.en.md", false)]
    [InlineData("docs/guide/changelog.zh-TW.md", false)]
    [InlineData("docs/guide/en/01-what-is-struocms.md", true)]
    [InlineData("docs/guide/zh-TW/21-schema-and-upgrades.md", true)]
    [InlineData("docs/ai/conventions.md", true)]
    [InlineData("docs/ai/decisions/pg-test-connection-pooling.md", true)]
    [InlineData("AGENTS.md", true)]
    [InlineData("CLAUDE.md", true)]
    [InlineData("src/Struo.Api/Program.cs", true)]
    [InlineData("tests/Struo.Tests/Documentation/RepositoryFiles.cs", true)]
    [InlineData("frontend/src/main.ts", true)]
    [InlineData("frontend/src/App.vue", true)]
    [InlineData("frontend/e2e/fixtures.ts", true)]
    [InlineData("README.md", false)]
    [InlineData("docs/guide/en/notes.txt", false)]
    [InlineData("samples/Struo.Sample.Blog/Article.cs", false)]
    public void Includes_scans_locale_folders_but_not_files_directly_under_guide(string path, bool expected)
    {
        NarrativeScanScope.Includes(path).Should().Be(expected);
    }
}
