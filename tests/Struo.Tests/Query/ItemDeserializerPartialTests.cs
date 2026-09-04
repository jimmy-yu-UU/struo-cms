// tests/Struo.Tests/Query/ItemDeserializerPartialTests.cs
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Security;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// Direct unit tests of <see cref="ItemDeserializer.DeserializePartial"/>'s own contract — as opposed to
/// <c>JunctionPayloadWriteTests</c>, which exercises it only through <c>ItemWriteSideSync</c> and so
/// cannot distinguish its `onlyFields` intersection from the caller's own allowlist projection.
/// </summary>
public sealed class ItemDeserializerPartialTests
{
    private static ItemDeserializer NewDeserializer(JunctionPayloadHarness h) =>
        new(h.Registry, h.Graph, new RichTextCleaner(new GanssHtmlSanitizer()));

    [Fact]
    public void Only_the_requested_onlyFields_are_returned_even_when_more_keys_are_present()
    {
        using var h = new JunctionPayloadHarness();
        var deserializer = NewDeserializer(h);
        var meta = h.Metadata.GetCollection("jpLink")!;
        var onlyFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "note", "weight" };
        var body = JunctionPayloadHarness.Body(new
        {
            id = Guid.NewGuid(), note = "x", weight = 3,
            jpParentId = Guid.NewGuid(), sort = 1, bogus = 1,
        });

        var result = deserializer.DeserializePartial("jpLink", body, meta, onlyFields);

        result.Keys.Should().BeEquivalentTo(new[] { "Note", "Weight" });
    }

    [Fact]
    public void OnlyFields_absent_from_the_body_are_omitted_from_the_result()
    {
        using var h = new JunctionPayloadHarness();
        var deserializer = NewDeserializer(h);
        var meta = h.Metadata.GetCollection("jpLink")!;
        var onlyFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "note", "weight" };
        var body = JunctionPayloadHarness.Body(new { id = Guid.NewGuid(), weight = 5 });

        var result = deserializer.DeserializePartial("jpLink", body, meta, onlyFields);

        result.Keys.Should().BeEquivalentTo(new[] { "Weight" });
    }

    [Fact]
    public void MaxLength_still_throws_on_a_partial_bind()
    {
        using var h = new JunctionPayloadHarness();
        var deserializer = NewDeserializer(h);
        var meta = h.Metadata.GetCollection("jpLink")!;
        var onlyFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "note" };
        var body = JunctionPayloadHarness.Body(new { id = Guid.NewGuid(), note = new string('x', 21) });

        var act = () => deserializer.DeserializePartial("jpLink", body, meta, onlyFields);

        act.Should().Throw<QueryException>().WithMessage("*note*maximum length*");
    }

    [Fact]
    public void A_required_field_absent_from_the_partial_body_does_not_throw()
    {
        // jpChild.Name is Required=true; DeserializePartial must not enforce Required at all — a
        // junction payload object only sets the fields it names.
        using var h = new JunctionPayloadHarness();
        var deserializer = NewDeserializer(h);
        var meta = h.Metadata.GetCollection("jpChild")!;
        var onlyFields = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "name" };

        var result = deserializer.DeserializePartial("jpChild", JunctionPayloadHarness.Body(new { }), meta, onlyFields);

        result.Should().BeEmpty();
    }
}
