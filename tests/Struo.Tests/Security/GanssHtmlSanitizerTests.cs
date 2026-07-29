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
    public void Strips_iframe()
    {
        var clean = _s.Sanitize("<iframe src=\"http://evil\"></iframe><p>t</p>");
        clean.Should().NotContain("iframe");
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

    // PostProcessNode must strip a client-supplied `target` from
    // every surviving anchor, not just add rel=noopener — a lingering target="_blank" combined
    // with a stripped rel is the classic reverse-tabnabbing gap.
    [Fact]
    public void Strips_target_from_anchors()
    {
        var clean = _s.Sanitize("<a href=\"https://ok\" target=\"_blank\">l</a>");
        clean.Should().NotContain("target");
        clean.Should().Contain("rel=").And.Contain("noopener");
    }

    // Obfuscated/dangerous URL schemes in an anchor's href must not survive sanitization,
    // regardless of case, embedded control characters, or use of data: URIs — while a normal
    // https link is preserved. Exercises the parser's URL/scheme handling, a common regression
    // surface across an AngleSharp major-version bump.
    [Theory]
    [InlineData("<a href=\"JaVaScRiPt:alert(1)\">x</a>")]
    [InlineData("<a href=\"java&#09;script:alert(1)\">x</a>")]
    [InlineData("<a href=\"data:text/html,<script>alert(1)</script>\">x</a>")]
    public void Neutralizes_obfuscated_dangerous_href_schemes(string html)
    {
        var clean = _s.Sanitize(html);
        clean.Should().NotContain("javascript:", "no live javascript: link may survive")
             .And.NotContain("data:text/html", "no live data:text/html link may survive")
             .And.NotContain("<script", "no executable payload may survive");
    }

    [Fact]
    public void Preserves_normal_https_anchor_alongside_dangerous_ones()
    {
        var clean = _s.Sanitize("<a href=\"https://ok\">good</a><a href=\"javascript:alert(1)\">bad</a>");
        clean.Should().Contain("href=\"https://ok\"").And.Contain("good");
        clean.Should().NotContain("javascript:");
    }

    // mXSS via foreign-content (svg/math) parsing quirks — a common regression class when
    // a parser is upgraded, since these payloads rely on the HTML parser's foreign-content
    // insertion-mode handling (e.g. re-parsing <style>/<title> contents as HTML once serialized).
    [Theory]
    [InlineData("<svg><style><img src=x onerror=alert(1)></style></svg>")]
    [InlineData("<svg><script>alert(1)</script></svg>")]
    [InlineData("<math><mtext><table><mglyph><style><img src=x onerror=alert(1)></style></mglyph></table></mtext></math>")]
    public void Neutralizes_foreign_content_mxss_payloads(string html)
    {
        var clean = _s.Sanitize(html);
        clean.Should().NotContain("<script", "no script tag may survive")
             .And.NotContain("onerror=", "no event-handler attribute may survive")
             .And.NotContain("<svg", "the disallowed foreign-content tag itself must be stripped")
             .And.NotContain("<math");
    }

    [Fact]
    public void Keeps_basic_table_markup()
    {
        var html = "<table><tbody><tr><th><p>H</p></th></tr>"
                 + "<tr><td><p>c</p></td></tr></tbody></table>";
        var clean = _s.Sanitize(html);
        clean.Should().Contain("<table>").And.Contain("<tbody>").And.Contain("<tr>")
             .And.Contain("<th>").And.Contain("<td>");
    }

    [Fact]
    public void Strips_colspan_rowspan_and_colwidth_from_table_cells()
    {
        var clean = _s.Sanitize(
            "<table><tbody><tr><td colspan=\"2\" rowspan=\"3\" colwidth=\"120\"><p>c</p></td></tr></tbody></table>");
        clean.Should().NotContain("colspan").And.NotContain("rowspan").And.NotContain("colwidth");
        clean.Should().Contain("<td>");
    }

    [Fact]
    public void Keeps_text_align_style_on_paragraphs_and_headings()
    {
        var clean = _s.Sanitize(
            "<p style=\"text-align: center\">t</p><h2 style=\"text-align: right\">h</h2>");
        clean.Should().Contain("text-align").And.Contain("center").And.Contain("right");
    }

    [Fact]
    public void Keeps_color_style_on_span()
    {
        var clean = _s.Sanitize("<p><span style=\"color: #e11d48\">red</span></p>");
        clean.Should().Contain("<span").And.Contain("color");
        clean.Should().Contain("red");
    }

    [Fact]
    public void Strips_non_allowlisted_css_properties_but_keeps_allowed_ones()
    {
        var clean = _s.Sanitize(
            "<p style=\"text-align: center; position: fixed; font-size: 99px\">t</p>");
        clean.Should().Contain("text-align").And.NotContain("position").And.NotContain("font-size");
    }

    [Fact]
    public void Strips_dangerous_css_values()
    {
        var clean = _s.Sanitize(
            "<p style=\"background: url(//evil/x.png)\">a</p>"
          + "<p style=\"width: expression(alert(1))\">b</p>");
        clean.Should().NotContain("url(").And.NotContain("expression");
        clean.Should().Contain("a").And.Contain("b");
    }

    [Fact]
    public void Strips_dangerous_values_inside_allowed_css_properties()
    {
        var clean = _s.Sanitize(
            "<p><span style=\"color: expression(alert(1))\">a</span></p>"
          + "<p style=\"text-align: url(javascript:alert(1))\">b</p>"
          + "<p><span style=\"color: red\">ok</span></p>");
        clean.Should().NotContain("expression").And.NotContain("url(").And.NotContain("javascript");
        clean.Should().Contain("a").And.Contain("b");
        clean.Should().Contain("color").And.Contain("ok"); // benign value inside an allowed property survives
    }

    [Fact]
    public void Keeps_sub_and_sup()
    {
        var clean = _s.Sanitize("<p>H<sub>2</sub>O and x<sup>2</sup></p>");
        clean.Should().Contain("<sub>").And.Contain("<sup>");
    }
}
