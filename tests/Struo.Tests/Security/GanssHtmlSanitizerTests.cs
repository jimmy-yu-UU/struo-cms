using AwesomeAssertions;
using Struo.Infrastructure.Security;
using Xunit;

namespace Struo.Tests.Security;

public class GanssHtmlSanitizerTests
{
    private readonly GanssHtmlSanitizer _s = new();

    [Fact]
    public void Keeps_allowlisted_formatting_tags()
    {
        var html = "<h2>Title</h2><p><strong>bold</strong> <em>i</em> <s>x</s></p>"
                 + "<ul><li>a</li></ul><ol><li>b</li></ol><blockquote>q</blockquote>"
                 + "<pre><code>code</code></pre><hr>";
        var clean = _s.Sanitize(html);
        clean.Should().Contain("<h2>").And.Contain("<strong>").And.Contain("<em>")
             .And.Contain("<s>").And.Contain("<ul>").And.Contain("<ol>")
             .And.Contain("<blockquote>").And.Contain("<pre>").And.Contain("<code>").And.Contain("<hr");
    }

    [Fact]
    public void Strips_script_and_event_handlers()
    {
        var clean = _s.Sanitize("<p onclick=\"steal()\">hi</p><script>alert(1)</script>");
        clean.Should().NotContain("script").And.NotContain("onclick");
        clean.Should().Contain("hi");
    }

    [Fact]
    public void Strips_iframe_and_inline_style()
    {
        var clean = _s.Sanitize("<iframe src=\"http://evil\"></iframe><p style=\"color:red\">t</p>");
        clean.Should().NotContain("iframe").And.NotContain("style=");
        clean.Should().Contain("t");
    }

    [Theory]
    [InlineData("<a href=\"http://ok\">l</a>", true)]
    [InlineData("<a href=\"https://ok\">l</a>", true)]
    [InlineData("<a href=\"mailto:a@b.c\">l</a>", true)]
    [InlineData("<a href=\"javascript:alert(1)\">l</a>", false)]
    public void Enforces_anchor_scheme_allowlist(string html, bool keepsHref)
    {
        var clean = _s.Sanitize(html);
        if (keepsHref) clean.Should().Contain("href=");
        else clean.Should().NotContain("javascript");
    }

    [Fact]
    public void Keeps_relative_image_with_data_file_id_but_drops_unsafe_src()
    {
        var ok = _s.Sanitize("<img src=\"/api/files/abc/content\" data-file-id=\"abc\" alt=\"x\">");
        ok.Should().Contain("src=\"/api/files/abc/content\"").And.Contain("data-file-id=\"abc\"").And.Contain("alt=\"x\"");

        var bad = _s.Sanitize("<img src=\"javascript:alert(1)\"><img src=\"data:text/html;base64,PHN2Zz4=\">");
        bad.Should().NotContain("javascript").And.NotContain("data:");
    }

    [Fact]
    public void Adds_rel_noopener_to_anchors()
    {
        var clean = _s.Sanitize("<a href=\"https://ok\">l</a>");
        clean.Should().Contain("rel=").And.Contain("noopener");
    }
}
