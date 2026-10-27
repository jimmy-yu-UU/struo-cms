using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Application.Query.Write;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Security;
using Struo.Sample.Blog;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// <see cref="ItemDeserializer"/> reads a written date-time as a UTC instant. Pinned here because
/// a UTC host reads an offset value as <see cref="DateTimeKind.Local"/> with UTC digits, so the
/// API tests alone stay green without the converter.
/// </summary>
public sealed class ItemDeserializerUtcInstantTests
{
    private static (ItemDeserializer Deserializer, CollectionMetadata Meta) Build()
    {
        var types = new[] { typeof(Article), typeof(Category), typeof(Tag) };
        var collections = MetadataScanner.ScanTypes(types);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"] = typeof(Article), ["category"] = typeof(Category), ["tag"] = typeof(Tag),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var meta = new CachedMetadataProvider(collections).GetCollection("article")!;
        return (new ItemDeserializer(registry, graph, new RichTextCleaner(new GanssHtmlSanitizer())), meta);
    }

    private static JsonElement Body(string publishedAtJson) =>
        JsonDocument.Parse($$"""{"status":"draft","publishedAt":{{publishedAtJson}}}""").RootElement.Clone();

    [Fact]
    public void An_offset_value_binds_as_the_same_utc_instant()
    {
        var (deserializer, meta) = Build();

        var article = (Article)deserializer.DeserializeForCreate("article", Body("\"2026-10-14T10:00:00+08:00\""), meta);

        article.PublishedAt.Should().Be(new DateTime(2026, 10, 14, 2, 0, 0, DateTimeKind.Utc));
        article.PublishedAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Theory]
    [InlineData("12")]
    [InlineData("true")]
    [InlineData("{}")]
    [InlineData("\"not a date\"")]
    public void A_value_that_is_not_a_date_names_the_field(string json)
    {
        var (deserializer, meta) = Build();

        var act = () => deserializer.DeserializeForCreate("article", Body(json), meta);

        act.Should().Throw<QueryException>().WithMessage("Field 'publishedAt' has an invalid value.");
    }
}
