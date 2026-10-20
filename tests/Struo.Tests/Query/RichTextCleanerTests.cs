// tests/Struo.Tests/Query/RichTextCleanerTests.cs
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Infrastructure.Security;
using Xunit;

namespace Struo.Tests.Query;

/// <summary><see cref="RichTextCleaner.Sanitize"/>'s blank-document coercion, which strips tags to decide.</summary>
public sealed class RichTextCleanerTests
{
    private readonly RichTextCleaner _cleaner = new(new GanssHtmlSanitizer());

    [Theory]
    [InlineData("<p></p>")]
    [InlineData("<p>&nbsp;</p><b> </b>")]
    public void Tag_only_documents_become_null(string html) =>
        _cleaner.Sanitize(html).Should().BeNull();

    [Fact]
    public void Documents_with_text_are_kept() =>
        _cleaner.Sanitize("<p>a</p>").Should().Be("<p>a</p>");

    [Fact]
    public void Null_stays_null() => _cleaner.Sanitize(null).Should().BeNull();
}
