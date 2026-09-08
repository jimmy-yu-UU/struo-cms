// tests/Struo.Tests/Query/ItemDeserializerShapeErrorTests.cs
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Security;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// <see cref="ItemDeserializer.DeserializeElement"/>'s field-level message when a JSON value is the
/// wrong shape for its bound property (#14) — as opposed to <see cref="ItemDeserializerPartialTests"/>,
/// which exercises <c>DeserializePartial</c>'s allowlist/presence contract, not this error path.
/// </summary>
public sealed class ItemDeserializerShapeErrorTests
{
    private static ItemDeserializer NewDeserializer(JunctionPayloadHarness h) =>
        new(h.Registry, h.Graph, new RichTextCleaner(new GanssHtmlSanitizer()));

    [Fact]
    public void Empty_string_for_an_int_field_names_the_field()
    {
        using var h = new JunctionPayloadHarness();
        var deserializer = NewDeserializer(h);
        var meta = h.Metadata.GetCollection("jpLink")!;
        var onlyFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "weight" };
        var body = JunctionPayloadHarness.Body(new { id = Guid.NewGuid(), weight = "" });

        var act = () => deserializer.DeserializePartial("jpLink", body, meta, onlyFields);

        act.Should().Throw<QueryException>().WithMessage("Field 'weight' has an invalid value.");
    }

    [Fact]
    public void A_number_for_a_string_field_names_the_field()
    {
        using var h = new JunctionPayloadHarness();
        var deserializer = NewDeserializer(h);
        var meta = h.Metadata.GetCollection("jpLink")!;
        var onlyFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "note" };
        var body = JunctionPayloadHarness.Body(new { id = Guid.NewGuid(), note = 5 });

        var act = () => deserializer.DeserializePartial("jpLink", body, meta, onlyFields);

        act.Should().Throw<QueryException>().WithMessage("Field 'note' has an invalid value.");
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("$", null)]
    [InlineData("$.weight", "weight")]
    [InlineData("$.faqs[0].answer", "faqs[0].answer")]
    public void FieldFromJsonPath_maps_a_System_Text_Json_path_to_a_field_name(string? path, string? expected) =>
        ItemDeserializer.FieldFromJsonPath(path).Should().Be(expected);
}
