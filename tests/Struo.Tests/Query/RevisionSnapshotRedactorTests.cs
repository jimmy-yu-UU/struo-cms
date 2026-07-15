// tests/Struo.Tests/Query/RevisionSnapshotRedactorTests.cs
using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// Unit tests for <see cref="RevisionSnapshotRedactor"/> (SEC-2): the snapshot
/// <see cref="RevisionSnapshotBuilder"/> produces deliberately includes every field, including ones
/// marked <see cref="FieldMetadata.Hidden"/> (a revert must be able to restore them). This class
/// verifies the redaction rewrite applied before a snapshot is handed to an external caller.
/// </summary>
public sealed class RevisionSnapshotRedactorTests
{
    /// <summary>A mix of a hidden own-field, a hidden translatable field, and non-hidden fields of
    /// several JSON shapes (mirrors an Article-shaped snapshot).</summary>
    private static readonly CollectionMetadata Meta = new()
    {
        Name = "article", Label = "Article", FieldGroups = [],
        Fields =
        [
            new FieldMetadata { Name = "status", Label = "Status", Interface = FieldInterface.Text },
            new FieldMetadata { Name = "internalNote", Label = "Internal Note", Interface = FieldInterface.Text, Hidden = true },
            new FieldMetadata { Name = "title", Label = "Title", Interface = FieldInterface.Text, Translatable = true },
            new FieldMetadata { Name = "internalSlug", Label = "Internal Slug", Interface = FieldInterface.Text, Hidden = true, Translatable = true },
        ]
    };

    private static readonly CollectionMetadata MetaNoHidden = new()
    {
        Name = "plain", Label = "Plain", FieldGroups = [],
        Fields = [new FieldMetadata { Name = "status", Label = "Status", Interface = FieldInterface.Text }]
    };

    [Fact]
    public void Removes_hidden_top_level_key()
    {
        var json = """{"id":"1","status":"draft","internalNote":"secret-token"}""";
        var redacted = RevisionSnapshotRedactor.RedactHidden(json, Meta);

        using var doc = JsonDocument.Parse(redacted);
        doc.RootElement.TryGetProperty("internalNote", out _).Should().BeFalse();
        doc.RootElement.GetProperty("status").GetString().Should().Be("draft");
    }

    [Fact]
    public void Removes_hidden_translatable_key_from_every_locale()
    {
        var json = """
            {
              "id":"1","status":"draft",
              "translations": {
                "en": {"title":"T-en","internalSlug":"secret-en"},
                "zh-TW": {"title":"T-zh","internalSlug":"secret-zh"}
              }
            }
            """;
        var redacted = RevisionSnapshotRedactor.RedactHidden(json, Meta);

        using var doc = JsonDocument.Parse(redacted);
        var translations = doc.RootElement.GetProperty("translations");
        translations.GetProperty("en").TryGetProperty("internalSlug", out _).Should().BeFalse();
        translations.GetProperty("en").GetProperty("title").GetString().Should().Be("T-en");
        translations.GetProperty("zh-TW").TryGetProperty("internalSlug", out _).Should().BeFalse();
        translations.GetProperty("zh-TW").GetProperty("title").GetString().Should().Be("T-zh");
    }

    [Fact]
    public void Preserves_non_hidden_content_byte_faithfully()
    {
        var json = """{"id":"1","count":42,"nested":{"a":[1,2,3]},"flag":true,"empty":null,"list":["x","y"]}""";
        var redacted = RevisionSnapshotRedactor.RedactHidden(json, Meta);

        using var doc = JsonDocument.Parse(redacted);
        var root = doc.RootElement;
        root.GetProperty("count").GetInt32().Should().Be(42);
        root.GetProperty("nested").GetProperty("a").EnumerateArray().Select(e => e.GetInt32()).Should().Equal(1, 2, 3);
        root.GetProperty("flag").GetBoolean().Should().BeTrue();
        root.GetProperty("empty").ValueKind.Should().Be(JsonValueKind.Null);
        root.GetProperty("list").EnumerateArray().Select(e => e.GetString()).Should().Equal("x", "y");
    }

    [Fact]
    public void Handles_snapshot_without_translations()
    {
        var json = """{"id":"1","status":"draft","internalNote":"secret"}""";
        var redacted = RevisionSnapshotRedactor.RedactHidden(json, Meta);

        using var doc = JsonDocument.Parse(redacted);
        doc.RootElement.TryGetProperty("translations", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("internalNote", out _).Should().BeFalse();
    }

    [Fact]
    public void Meta_with_zero_hidden_fields_returns_equivalent_json()
    {
        var json = """{"id":"1","status":"draft"}""";
        var redacted = RevisionSnapshotRedactor.RedactHidden(json, MetaNoHidden);

        using var doc = JsonDocument.Parse(redacted);
        doc.RootElement.GetProperty("id").GetString().Should().Be("1");
        doc.RootElement.GetProperty("status").GetString().Should().Be("draft");
        doc.RootElement.EnumerateObject().Count().Should().Be(2);
    }

    [Fact]
    public void Does_not_mutate_input_string()
    {
        var json = """{"id":"1","internalNote":"secret"}""";
        var original = json;
        _ = RevisionSnapshotRedactor.RedactHidden(json, Meta);

        json.Should().Be(original); // strings are immutable in .NET, but assert the reference/content anyway
        json.Should().Contain("internalNote"); // input untouched
    }
}
