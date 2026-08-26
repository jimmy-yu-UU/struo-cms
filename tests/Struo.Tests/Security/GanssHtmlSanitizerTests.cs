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
    public void Keeps_heading_levels_two_through_six()
    {
        var clean = _s.Sanitize("<h2>a</h2><h3>b</h3><h4>c</h4><h5>d</h5><h6>e</h6>");
        clean.Should().Contain("<h2>").And.Contain("<h3>").And.Contain("<h4>")
             .And.Contain("<h5>").And.Contain("<h6>");
    }

    // The editor deliberately offers H2-H6 only: the page title is the H1, so a second H1 inside
    // body content would break the document outline. This asserts that choice instead of trusting
    // a comment to survive the next person who widens the allowlist.
    //
    // It also pins what removal actually does, measured rather than assumed: Ganss.Xss leaves
    // KeepChildNodes false, so a disallowed tag is dropped together with its subtree -- the text
    // inside the h1 does not survive as a bare paragraph.
    [Fact]
    public void Strips_h1_and_its_text()
    {
        var clean = _s.Sanitize("<h1>Heading one</h1><p>body</p>");
        clean.Should().NotContain("<h1");
        clean.Should().Be("<p>body</p>");
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

    // rel is derived from target, not client-supplied (chapter 5's RichText contract table): a
    // same-tab anchor -- the common case, and what a bare href with no target means -- carries no
    // rel at all. Asserted on the exact string, not Contain, because a same-tab anchor must NOT
    // pick up any rel value.
    [Fact]
    public void Bare_anchor_with_no_target_carries_no_rel()
    {
        var clean = _s.Sanitize("<a href=\"https://ok\">l</a>");
        clean.Should().Be("<a href=\"https://ok\">l</a>");
    }

    // target="_blank" is the one value allowed through, and it is paired with exactly rel="noopener"
    // -- not noreferrer, not nofollow, not both. That pairing is what makes narrowing the old
    // absolute strip safe: noopener is set on exactly the links that keep target="_blank".
    [Fact]
    public void Blank_target_survives_with_exactly_rel_noopener()
    {
        var clean = _s.Sanitize("<a href=\"https://ok\" target=\"_blank\">l</a>");
        clean.Should().Be("<a href=\"https://ok\" target=\"_blank\" rel=\"noopener\">l</a>");
    }

    // Any target value other than "_blank" is not a supported opt-in and is stripped along with its
    // rel -- exactly the case Adds_rel_noopener_to_anchors used to gloss over by only checking
    // Contain("rel=")/Contain("noopener"), which is why nobody noticed the old handler dropped
    // TipTap's "nofollow" silently. This asserts the exact surviving attribute set instead.
    [Fact]
    public void Non_blank_target_is_stripped_along_with_its_rel()
    {
        var clean = _s.Sanitize("<a href=\"https://ok\" target=\"_top\">l</a>");
        clean.Should().Be("<a href=\"https://ok\">l</a>");
    }

    // rel is backend-owned and derived from target: a client-supplied rel is overwritten, never
    // merged or trusted, so the security property cannot go missing because a writer forgot it.
    [Fact]
    public void Client_supplied_rel_is_overwritten_by_the_derived_value()
    {
        var blank = _s.Sanitize("<a href=\"https://ok\" target=\"_blank\" rel=\"nofollow\">l</a>");
        blank.Should().Be("<a href=\"https://ok\" target=\"_blank\" rel=\"noopener\">l</a>");

        var sameTab = _s.Sanitize("<a href=\"https://ok\" rel=\"nofollow\">l</a>");
        sameTab.Should().Be("<a href=\"https://ok\">l</a>");
    }

    // A rejected href (disallowed scheme) still leaves the <a> element itself in the tree with no
    // href attribute: the sanitizer strips only the offending attribute, not the whole element. This
    // is not a security fix -- an unguarded rule would still pair target="_blank" with rel="noopener"
    // here, so no reverse-tabnabbing gap opens either way -- it is hygiene: an anchor with no href is
    // not a link, so it should not carry link-behaviour attributes regardless of what target it wears.
    [Fact]
    public void Anchor_with_rejected_href_does_not_keep_target()
    {
        var clean = _s.Sanitize("<a href=\"javascript:alert(1)\" target=\"_blank\">l</a>");
        clean.Should().NotContain("target");
        clean.Should().NotContain("rel=");
        clean.Should().NotContain("javascript");
    }

    // target is allowlisted globally (needed so the anchor handler above can see it at all), which
    // means it would otherwise ride through untouched on every other allowlisted tag -- inert today
    // since no other allowed tag gives it meaning, but stored-HTML pollution that would matter the day
    // AllowedTags grows a tag target does mean something on. Stripped from every non-anchor element.
    [Theory]
    [InlineData("<p target=\"_blank\">x</p>", "<p>x</p>")]
    [InlineData("<span target=\"evil\">s</span>", "<span>s</span>")]
    [InlineData("<img src=\"/a\" target=\"_blank\">", "<img src=\"/a\">")]
    public void Strips_target_from_non_anchor_elements(string html, string expected)
    {
        _s.Sanitize(html).Should().Be(expected);
    }

    // The "_blank" match is exact-string and case-sensitive, deliberately fail-closed: the HTML
    // Standard's browsing-context keyword matching is ASCII case-insensitive
    // (https://html.spec.whatwg.org/multipage/links.html), so an unsanitized target="_BLANK" would
    // still open a new tab in a real browser -- the opposite of what this handler does with it. A
    // fork loosening this comparison to match the browser's own case-insensitivity would silently
    // start granting rel="noopener" to values it does not today, with no other test catching it.
    [Theory]
    [InlineData("_Blank")]
    [InlineData("_BLANK")]
    [InlineData("_blank ")]
    [InlineData("_top")]
    [InlineData("_self")]
    [InlineData("myframe")]
    public void Non_exact_blank_values_are_treated_as_same_tab(string targetValue)
    {
        var clean = _s.Sanitize($"<a href=\"https://ok\" target=\"{targetValue}\">l</a>");
        clean.Should().Be("<a href=\"https://ok\">l</a>");
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

    // TipTap's table extension has no <thead> node in its schema and its renderHTML is hard-coded to
    // ["table", attrs, colgroup, ["tbody", 0]], so the admin SPA cannot emit a header section no
    // matter what it does. The stored HTML is what every fork's frontend renders, and
    // @tailwindcss/typography's header rules key off `thead th`, so the header row has to be wrapped
    // server-side. Doing it here rather than in the SPA also covers the write paths that never touch
    // TipTap at all -- an import tool, an ETL, a direct POST.
    [Fact]
    public void Wraps_an_all_th_first_row_in_thead()
    {
        var clean = _s.Sanitize(
            "<table><tbody><tr><th>A</th><th>B</th></tr><tr><td>1</td><td>2</td></tr></tbody></table>");
        clean.Should().Be(
            "<table><thead><tr><th>A</th><th>B</th></tr></thead><tbody><tr><td>1</td><td>2</td></tr></tbody></table>");
    }

    // A row that mixes th and td is not a header row -- wrapping it would change how a frontend
    // renders content the author did not mark up as a header.
    [Fact]
    public void Leaves_a_mixed_first_row_alone()
    {
        var html = "<table><tbody><tr><th>A</th><td>1</td></tr></tbody></table>";
        _s.Sanitize(html).Should().Be(html);
    }

    // Only the FIRST row is a candidate. A th row further down is something else (a sub-heading
    // inside the body) and moving it would reorder the table.
    [Fact]
    public void Leaves_a_th_row_that_is_not_first_alone()
    {
        var html = "<table><tbody><tr><td>1</td></tr><tr><th>A</th></tr></tbody></table>";
        _s.Sanitize(html).Should().Be(html);
    }

    // Content that already carries a thead (an import, or a second save of already-normalized
    // content) must come through untouched -- this is the same property as idempotence, asserted
    // from the input side.
    [Fact]
    public void Leaves_an_existing_thead_alone()
    {
        var html = "<table><thead><tr><th>A</th></tr></thead><tbody><tr><td>1</td></tr></tbody></table>";
        _s.Sanitize(html).Should().Be(html);
    }

    // Sanitize runs on every write, so a value that round-trips through several saves must not
    // accumulate changes.
    [Fact]
    public void Normalization_is_idempotent()
    {
        var once = _s.Sanitize(
            "<table><tbody><tr><th>A</th></tr><tr><td>1</td></tr></tbody></table>");
        _s.Sanitize(once).Should().Be(once);
    }

    // Real stored HTML is not minified. Whitespace between rows arrives as text nodes in the DOM,
    // so "the first row" and "every cell is a th" must both be decided over ELEMENT children --
    // a naive childNodes walk sees the newlines and decides the row is mixed.
    [Fact]
    public void Wraps_the_header_row_despite_whitespace_between_rows()
    {
        var clean = _s.Sanitize(
            "<table>\n  <tbody>\n    <tr>\n      <th>A</th>\n    </tr>\n    <tr>\n      <td>1</td>\n    </tr>\n  </tbody>\n</table>");
        clean.Should().Contain("<thead>").And.Contain("<th>A</th>");

        // Structural, not textual: the input is never minified, so a substring check against an
        // exact unminified fragment can never fail regardless of correctness. Assert instead that
        // </thead> closes before <tbody> opens, and that the surviving row is still inside that
        // tbody -- this is the assertion that would actually fail if the wrap misplaced the section.
        var theadClose = clean.IndexOf("</thead>", StringComparison.Ordinal);
        var tbodyOpen = clean.IndexOf("<tbody>", StringComparison.Ordinal);
        var tbodyClose = clean.IndexOf("</tbody>", StringComparison.Ordinal);
        theadClose.Should().BeGreaterThan(-1);
        tbodyOpen.Should().BeGreaterThan(theadClose);
        tbodyClose.Should().BeGreaterThan(tbodyOpen);
        clean[tbodyOpen..tbodyClose].Should().Contain("<td>1</td>");
    }

    // A header-only table leaves an empty tbody behind. Emitting <tbody></tbody> into every fork's
    // stored content is noise, so it is removed -- but only when the wrap is what emptied it.
    [Fact]
    public void Removes_the_tbody_when_the_header_row_was_its_only_row()
    {
        var clean = _s.Sanitize("<table><tbody><tr><th>A</th><th>B</th></tr></tbody></table>");
        clean.Should().Be("<table><thead><tr><th>A</th><th>B</th></tr></thead></table>");
    }

    // Each table is independent; a nested table must not be skipped or double-processed.
    [Fact]
    public void Normalizes_a_nested_table_independently()
    {
        var clean = _s.Sanitize(
            "<table><tbody><tr><th>Outer</th></tr><tr><td>"
          + "<table><tbody><tr><th>Inner</th></tr><tr><td>x</td></tr></tbody></table>"
          + "</td></tr></tbody></table>");
        clean.Should().Contain("<thead><tr><th>Outer</th></tr></thead>");
        clean.Should().Contain("<thead><tr><th>Inner</th></tr></thead>");
    }

    // Every table test above supplies an explicit <tbody>, i.e. exactly what TipTap's renderHTML
    // emits. But the reason this normalization lives on the server at all is the writers that are
    // NOT TipTap -- imports, ETL, direct API POSTs -- which routinely hand-author
    // <table><tr><th>...</tr></table> with no <tbody> at all. The HTML parser is expected to
    // synthesize a tbody before the direct-children lookup ever runs, but that is a claim about
    // AngleSharp's tree construction, not this normalizer's own logic, so it is asserted here
    // rather than taken on trust.
    [Fact]
    public void Wraps_the_header_row_when_the_source_has_no_explicit_tbody()
    {
        var clean = _s.Sanitize("<table><tr><th>A</th></tr><tr><td>1</td></tr></table>");
        clean.Should().Contain("<thead>").And.Contain("<th>A</th>");
        clean.Should().Contain("<td>1</td>");
    }

    // Nothing table-shaped must be disturbed, and an empty table must not throw.
    [Fact]
    public void Leaves_non_table_content_and_empty_tables_alone()
    {
        _s.Sanitize("<p>plain</p>").Should().Be("<p>plain</p>");
        _s.Sanitize("<table></table>").Should().Be("<table></table>");
    }

    // width is allowlisted globally (AllowedAttributes has no per-tag concept), so an integer-only
    // shape check has to be applied in the PostProcessNode handler -- the same reasoning as target's
    // anchor-only narrowing above. Recorded observation from this task (RT-6 task 1): a throwaway
    // HtmlSanitizer with "width" on AllowedAttributes and no PostProcessNode handler at all let
    // width="abc" survive Sanitize() untouched, so Ganss.Xss does not itself validate attribute
    // VALUES once a name is on AllowedAttributes -- nothing upstream of this handler will ever
    // reject a malformed width for us.
    [Fact]
    public void Keeps_integer_width_on_an_image()
    {
        var clean = _s.Sanitize("<img src=\"https://e.com/a.png\" width=\"480\">");
        clean.Should().Be("<img src=\"https://e.com/a.png\" width=\"480\">");
    }

    // Anything that is not exactly one-to-five ASCII digits with no leading zero is rejected,
    // including shapes a naive int.TryParse would accept (leading whitespace) or that are
    // dimensionally meaningless for a fixed pixel width (a percentage, zero, negative, a decimal).
    // src surviving alongside the stripped width is the proof this is attribute-level stripping,
    // not the whole <img> being dropped.
    //
    // The trailing "480\n" and "480\r\n" cases guard a real .NET regex pitfall: $ (without
    // RegexOptions.Multiline) matches at end-of-string OR immediately before one trailing '\n', so
    // an anchor pair of ^...$ lets "480\n" through -- reachable in real input via an attribute value
    // decoded from "480&#10;". Confirmed directly by running both cases against the sanitizer with
    // the old ^[1-9][0-9]{0,4}$ pattern still in place: BOTH failed (width survived) -- not only the
    // bare "480\n" case but also "480\r\n", because the HTML5 input-stream preprocessing AngleSharp
    // performs collapses a raw \r\n (and a lone \r) down to \n before the attribute value ever
    // reaches this handler, so by the time the regex runs both inputs are the identical string
    // "480\n". Both pass closed under \A/\z. Kept as two separate cases anyway since they exercise
    // different input bytes even though they collapse to the same decoded value.
    [Theory]
    [InlineData("abc")]
    [InlineData("40%")]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("480.5")]
    [InlineData(" 480")]
    [InlineData("")]
    [InlineData("100000")]
    [InlineData("480\n")]
    [InlineData("480\r\n")]
    public void Strips_non_integer_width_on_an_image(string widthValue)
    {
        var clean = _s.Sanitize($"<img src=\"https://e.com/a.png\" width=\"{widthValue}\">");
        clean.Should().NotContain("width");
        clean.Should().Contain("src=\"https://e.com/a.png\"");
    }

    // width surviving on anything other than <img> would let a fork's non-image renderer (a <td>,
    // a <table>, a plain paragraph) receive a raw author-controlled pixel width it was never
    // designed to defend against -- the img-only shape check is not enough by itself.
    //
    // The <td> case is wrapped in a real <table><tbody><tr>...</tr></tbody></table> rather than
    // handed to Sanitize bare: a bare "<td width=\"200\">c</td>" is a parse error in body-context
    // fragment parsing, so AngleSharp never materializes a <td> element from it at all and the row
    // would pass even with the non-image width strip deleted. Verified directly (RT-6 task 1 fix
    // round): with that strip temporarily commented out, the bare-<td> row alone still passed while
    // the other four rows in this theory correctly failed -- confirming the bare form was vacuous.
    [Theory]
    [InlineData("<table><tbody><tr><td width=\"200\">c</td></tr></tbody></table>")]
    [InlineData("<table width=\"100%\"></table>")]
    [InlineData("<p width=\"480\">t</p>")]
    [InlineData("<span width=\"480\">s</span>")]
    [InlineData("<a href=\"https://e.com\" width=\"480\">l</a>")]
    public void Strips_width_from_non_image_elements(string html)
    {
        _s.Sanitize(html).Should().NotContain("width");
    }

    // Pins the deliberate absence of height from AllowedAttributes: a fork's frontend is not
    // guaranteed to pair a stored width with height:auto, and width+height together on a container
    // that only caps max-width:100% would squash the image's aspect ratio. This must fail under the
    // mutation "add height to AllowedAttributes with no further handling" -- verified out of band
    // (Task 1): that exact edit was made temporarily against this file, this test alone was re-run
    // and failed on the height assertion, then the edit was reverted.
    [Fact]
    public void Never_keeps_height_on_an_image()
    {
        var clean = _s.Sanitize("<img src=\"https://e.com/a.png\" width=\"480\" height=\"320\">");
        clean.Should().Contain("width=\"480\"");
        clean.Should().NotContain("height");
    }

    // Regression lock: the new width branch must not shadow the existing anchor-vs-non-anchor
    // target handling. An <img> is not an IHtmlAnchorElement, so it must still fall into the
    // "strip target" side of the handler.
    [Fact]
    public void Still_removes_target_from_an_image()
    {
        var clean = _s.Sanitize("<img src=\"https://e.com/a.png\" target=\"_blank\">");
        clean.Should().NotContain("target");
    }

}
