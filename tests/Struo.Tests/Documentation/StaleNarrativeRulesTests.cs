using AwesomeAssertions;

namespace Struo.Tests.Documentation;

public sealed class StaleNarrativeRulesTests
{
    [Fact]
    public void Extract_CSharp_keeps_only_comment_text()
    {
        string[] lines =
        [
            "var s = \"used to\";",
            "// used to be X",
            "/// <summary>previously</summary>",
            "codeBefore /* first",
            "second",
            "third */ codeAfter",
        ];

        var scanned = StaleNarrativeRules.ExtractScannable(StaleNarrativeRules.SourceKind.CSharp, lines);

        scanned[0].Text.Should().BeEmpty();
        scanned[1].Text.Should().Be("used to be X");
        scanned[2].Text.Should().Contain("previously");
        scanned[2].Text.Should().NotStartWith("/");
        scanned[3].Text.Should().Be("first");
        scanned[4].Text.Should().Be("second");
        scanned[5].Text.Should().Be("third");

        // A block-comment opener inside a string literal is not a real comment start.
        var stringLiteralBlockComment = StaleNarrativeRules.ExtractScannable(
            StaleNarrativeRules.SourceKind.CSharp, ["var s = \"/* used to */\";"]);
        stringLiteralBlockComment[0].Text.Should().BeEmpty();

        // The same string literal followed by a real line comment must not be mistaken for an
        // unterminated block comment that would swallow every following line.
        var blockOpenerInStringThenRealComment = StaleNarrativeRules.ExtractScannable(
            StaleNarrativeRules.SourceKind.CSharp,
            ["const s = \"/*\"; // real comment", "var x = 1;"]);
        blockOpenerInStringThenRealComment[0].Text.Should().Be("real comment");
        blockOpenerInStringThenRealComment[1].Text.Should().BeEmpty();

        // A "//" inside a URL string literal, followed by a real trailing comment.
        var urlThenRealComment = StaleNarrativeRules.ExtractScannable(
            StaleNarrativeRules.SourceKind.CSharp, ["const url = \"http://x\"; // real comment"]);
        urlThenRealComment[0].Text.Should().Be("real comment");

        // A "//" that only ever appears inside a string literal is not a comment at all.
        var lineCommentInsideStringLiteral = StaleNarrativeRules.ExtractScannable(
            StaleNarrativeRules.SourceKind.CSharp, ["var s = \"a // used to\";"]);
        lineCommentInsideStringLiteral[0].Text.Should().BeEmpty();
    }

    [Fact]
    public void Extract_TypeScript_and_Vue_comments()
    {
        string[] tsLines =
        [
            "const s = 'no longer';",
            "// previously handled here",
            "/* formerly a fallback */",
            "const url = \"http://x\"; // real comment",
        ];
        string[] vueLines = ["<!-- previously used here -->"];

        var ts = StaleNarrativeRules.ExtractScannable(StaleNarrativeRules.SourceKind.TypeScript, tsLines);
        var vue = StaleNarrativeRules.ExtractScannable(StaleNarrativeRules.SourceKind.Vue, vueLines);

        ts[0].Text.Should().BeEmpty();
        ts[1].Text.Should().Be("previously handled here");
        ts[3].Text.Should().Be("real comment");
        ts[2].Text.Should().Be("formerly a fallback");
        vue[0].Text.Should().Be("previously used here");
    }

    [Fact]
    public void Extract_Markdown_drops_fences_and_code_spans()
    {
        string[] lines =
        [
            "## Evidence",
            "```",
            "previously in a fence",
            "```",
            "~~~",
            "still fenced",
            "~~~~",
            "prose after the fence",
            "inline `no longer` span removed",
        ];

        var scanned = StaleNarrativeRules.ExtractScannable(StaleNarrativeRules.SourceKind.Markdown, lines);

        scanned.Should().NotContain(l => l.Text.Contains("previously in a fence"));
        scanned.Should().NotContain(l => l.Text.Contains("still fenced"));
        var afterFence = scanned.Single(l => l.LineNumber == 8);
        afterFence.Text.Should().Be("prose after the fence");
        afterFence.Section.Should().Be("Evidence");
        var withSpan = scanned.Single(l => l.LineNumber == 9);
        withSpan.Text.Should().NotContain("no longer");

        // An ATX closing sequence on the heading itself must not become part of the section name.
        var closedHeading = StaleNarrativeRules.ExtractScannable(
            StaleNarrativeRules.SourceKind.Markdown, ["## Evidence ##", "measured 2026-08-05"]);
        closedHeading[1].Section.Should().Be("Evidence");
    }

    [Theory]
    [InlineData("Previously the field was optional.", true)]
    [InlineData("unprecedentedly stable behaviour.", false)]
    [InlineData("nowthat is not a phrase.", false)]
    [InlineData("now that the field is required.", true)]
    public void Banned_words_are_word_bounded_and_case_insensitive(string text, bool expectMatch)
    {
        var lines = new[] { new StaleNarrativeRules.ScannableLine(1, text, null) };

        var violations = StaleNarrativeRules.Check("doc.md", lines, isDecisionFile: false);

        violations.Any(v => v.Rule == 1).Should().Be(expectMatch);
    }

    [Theory]
    [InlineData("以前 this worked differently.")]
    [InlineData("現在不再 supported.")]
    public void Chinese_terms_are_substring_matches(string text)
    {
        var lines = new[] { new StaleNarrativeRules.ScannableLine(1, text, null) };

        var violations = StaleNarrativeRules.Check("doc.md", lines, isDecisionFile: false);

        violations.Should().ContainSingle(v => v.Rule == 1);
    }

    [Fact]
    public void Allow_marker_with_reason_clears_rules_1_and_2()
    {
        var withReason = new[]
        {
            new StaleNarrativeRules.ScannableLine(1,
                "no longer present rows are deleted  narrative-guard:allow: describes the diff algorithm", null),
        };
        var emptyReason = new[]
        {
            new StaleNarrativeRules.ScannableLine(1, "used to  narrative-guard:allow:", null),
        };
        var mdEmpty = new[]
        {
            new StaleNarrativeRules.ScannableLine(1, "Previously the field was required. <!-- narrative-guard:allow: -->", null),
        };
        var mdWithReason = new[]
        {
            new StaleNarrativeRules.ScannableLine(1,
                "Previously the field was required. <!-- narrative-guard:allow: names the anti-pattern -->", null),
        };

        StaleNarrativeRules.Check("a.cs", withReason, isDecisionFile: false).Should().BeEmpty();

        var emptyViolations = StaleNarrativeRules.Check("a.cs", emptyReason, isDecisionFile: false);
        emptyViolations.Select(v => v.Rule).Should().BeEquivalentTo([1, 6]);

        var mdEmptyViolations = StaleNarrativeRules.Check("a.md", mdEmpty, isDecisionFile: false);
        mdEmptyViolations.Select(v => v.Rule).Should().BeEquivalentTo([1, 6]);

        StaleNarrativeRules.Check("a.md", mdWithReason, isDecisionFile: false).Should().BeEmpty();
    }

    [Fact]
    public void Bare_dates_are_rule_2_except_in_decision_Evidence()
    {
        var csLine = new[] { new StaleNarrativeRules.ScannableLine(1, "measured 2026-08-05", null) };
        var mdEvidence = new[] { new StaleNarrativeRules.ScannableLine(1, "measured 2026-08-05", "Evidence") };
        var mdWhy = new[] { new StaleNarrativeRules.ScannableLine(1, "measured 2026-08-05", "Why") };
        var mdWithMarker = new[]
        {
            new StaleNarrativeRules.ScannableLine(1, "measured 2026-08-05 narrative-guard:allow: dated evidence", "Why"),
        };

        StaleNarrativeRules.Check("a.cs", csLine, isDecisionFile: false).Should().ContainSingle(v => v.Rule == 2);
        StaleNarrativeRules.Check("a.md", mdEvidence, isDecisionFile: true).Should().BeEmpty();
        StaleNarrativeRules.Check("a.md", mdWhy, isDecisionFile: true).Should().ContainSingle(v => v.Rule == 2);
        StaleNarrativeRules.Check("a.md", mdWithMarker, isDecisionFile: true).Should().BeEmpty();
    }

    [Theory]
    [InlineData("See PR #107 for context.", true)]
    [InlineData("pull/107 has the change.", true)]
    [InlineData("see #96 for the discussion.", true)]
    [InlineData("Border color is #fff and #60a5fa.", false)]
    [InlineData("## 4. Title", false)]
    public void PR_and_issue_references_are_rule_3_and_cannot_be_marked(string text, bool expectMatch)
    {
        var plain = new[] { new StaleNarrativeRules.ScannableLine(1, text, null) };
        var marked = new[] { new StaleNarrativeRules.ScannableLine(1, text + " narrative-guard:allow: reworded already", null) };

        StaleNarrativeRules.Check("a.md", plain, isDecisionFile: false).Any(v => v.Rule == 3).Should().Be(expectMatch);
        StaleNarrativeRules.Check("a.md", marked, isDecisionFile: false).Any(v => v.Rule == 3).Should().Be(expectMatch);
    }

    [Fact]
    public void Decision_structure_requires_six_headings_in_order()
    {
        string[] missingUnknowns =
        [
            "# A decision title",
            "",
            "## Decision",
            "text",
            "## Why",
            "text",
            "## Evidence",
            "text",
            "## Referenced from",
            "text",
        ];
        string[] correctOrder =
        [
            "# A decision title",
            "",
            "## Decision",
            "text",
            "## Why",
            "text",
            "## Evidence",
            "text",
            "## Unknowns",
            "text",
            "## Referenced from",
            "text",
        ];

        var violations = StaleNarrativeRules.CheckDecisionStructure("a.md", missingUnknowns);
        violations.Should().ContainSingle(v => v.Rule == 5 && v.Detail == "missing heading '## Unknowns'");

        StaleNarrativeRules.CheckDecisionStructure("a.md", correctOrder).Should().BeEmpty();

        // A fenced code sample that merely shows "## Decision" must not satisfy the real heading.
        string[] decisionOnlyInFence =
        [
            "# A decision title",
            "```",
            "## Decision",
            "```",
            "## Why",
            "text",
            "## Evidence",
            "text",
            "## Unknowns",
            "text",
            "## Referenced from",
            "text",
        ];
        StaleNarrativeRules.CheckDecisionStructure("a.md", decisionOnlyInFence)
            .Should().ContainSingle(v => v.Rule == 5 && v.Detail == "missing heading '## Decision'");
    }
}
