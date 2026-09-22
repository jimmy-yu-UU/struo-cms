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
    [InlineData("can't be used to infer", false)]
    [InlineData("the key used to sign", true)]
    [InlineData("It used to be red", true)]
    [InlineData("The previous implementation did a check-then-branch.", true)]
    [InlineData("A previous version of this test worked around it.", true)]
    [InlineData("The previous mapping had these swapped.", true)]
    [InlineData("Before this fix, the factory returned a bare client.", true)]
    [InlineData("same as before this change.", true)]
    [InlineData("exactly like before this feature existed.", true)]
    [InlineData("no delete path before this task.", true)]
    [InlineData("every other entry joins the previous entry inside the group.", false)]
    [InlineData("must be registered before this line runs.", false)]
    [InlineData("a page computed against the previous ordering.", false)]
    [InlineData("The old code turned offset into a page index.", true)]
    [InlineData("the old raw-transaction code opened its own transaction", true)]
    [InlineData("where the old $from-keyed rule wrongly hid it", true)]
    [InlineData("the shape the old guard treated as a no-op", true)]
    [InlineData("(the old allow-all stub now lives in the test project)", true)]
    [InlineData("The old cap rejected sibling relations.", true)]
    [InlineData("not the old message match", true)]
    [InlineData("the old implementation bound them as a class", true)]
    [InlineData("The old behaviour surprised new users.", true)]
    [InlineData("The old behavior surprised new users.", true)]
    [InlineData("The old assertion pinned a stale status code.", true)]
    [InlineData("The old condition never matched an empty string.", true)]
    [InlineData("The old regex rejected valid emails.", true)]
    [InlineData("The old logic double-counted the discount.", true)]
    [InlineData("The old check allowed an expired token.", true)]
    [InlineData("The old mapping swapped two fields.", true)]
    [InlineData("The old binding leaked the connection.", true)]
    [InlineData("Invalidate swaps in a fresh Lazy, not the old value", false)]
    [InlineData("still echoing the OLD version 0", false)]
    [InlineData("an offset computed against the OLD page size is meaningless", false)]
    [InlineData("blocked until the old row is purged", false)]
    [InlineData("the old session isn't revoked", false)]
    [InlineData("a tab still holding the old index.html", false)]
    [InlineData("it unmounts the old one and mounts a fresh one", false)]
    [InlineData("the old and the new array", false)]
    public void Banned_words_are_word_bounded_and_case_insensitive(string text, bool expectMatch)
    {
        var lines = new[] { new StaleNarrativeRules.ScannableLine(1, text, null) };

        var violations = StaleNarrativeRules.Check("doc.md", lines, isDecisionFile: false);

        violations.Any(v => v.Rule == 1).Should().Be(expectMatch);
    }

    [Theory]
    [InlineData("以前 this worked differently.", true)]
    [InlineData("現在不再 supported.", true)]
    [InlineData("保留原本的值", false)]
    [InlineData("原本是必填", true)]
    public void Chinese_terms_are_substring_matches(string text, bool expectMatch)
    {
        var lines = new[] { new StaleNarrativeRules.ScannableLine(1, text, null) };

        var violations = StaleNarrativeRules.Check("doc.md", lines, isDecisionFile: false);

        violations.Any(v => v.Rule == 1).Should().Be(expectMatch);
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
