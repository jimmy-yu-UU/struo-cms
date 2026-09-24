using AwesomeAssertions;

namespace Struo.Tests.Documentation;

public sealed class NarrativeScanScopeTests
{
    [Theory]
    [InlineData("docs/guide/en/changelog.md", false)]
    [InlineData("docs/guide/zh-TW/changelog.md", false)]
    [InlineData("docs/guide/index.md", true)]
    [InlineData("docs/guide/changelog.en.md", true)]
    [InlineData("docs/guide/en/01-what-is-struocms.md", true)]
    [InlineData("docs/guide/en/sub/changelog.md", true)]
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
    public void Includes_exempts_only_the_two_locale_changelogs(string path, bool expected)
    {
        NarrativeScanScope.Includes(path).Should().Be(expected);
    }
}
