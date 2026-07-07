using System.Text.Encodings.Web;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Domain.Metadata.Models;
using Xunit;

namespace Struo.Tests.Metadata;

public class TagItemTests
{
    // JsonSerializerDefaults.Web alone still escapes non-ASCII (e.g. CJK) as \uXXXX per the
    // default JavaScriptEncoder — Encoder is set here purely so this test's literal Unicode
    // expectations compare against unescaped output; TagItem itself is encoder-agnostic.
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    [Fact]
    public void Serializes_camelCase_and_omits_null_label()
    {
        JsonSerializer.Serialize(new TagItem("tech"), Web).Should().Be("""{"value":"tech"}""");
        JsonSerializer.Serialize(new TagItem("ai", "人工智慧"), Web)
            .Should().Be("""{"value":"ai","label":"人工智慧"}""");
    }

    [Fact]
    public void Deserializes_from_camelCase_object()
    {
        var t = JsonSerializer.Deserialize<TagItem>("""{"value":"ai","label":"人工智慧"}""", Web);
        t.Should().Be(new TagItem("ai", "人工智慧"));
        var bare = JsonSerializer.Deserialize<TagItem>("""{"value":"tech"}""", Web);
        bare.Should().Be(new TagItem("tech"));
    }
}
