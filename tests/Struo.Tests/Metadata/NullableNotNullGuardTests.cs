using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

// A nullable [CmsField] property always gets a nullable column, so [SugarColumn(IsNullable = false)]
// on it cannot take effect. The scan rejects the pair in collections, translation entities and
// junction collections.
public sealed class NullableNotNullGuardTests
{
    [CmsCollection("Nn guid", Name = "nnGuid")]
    public sealed class GuidCollection
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = false)]
        [CmsField(Label = "Owner", Interface = FieldInterface.Uuid)] public Guid? Owner { get; set; }
    }

    [CmsCollection("Nn string", Name = "nnString")]
    public sealed class StringCollection
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = false, Length = 50)]
        [CmsField(Label = "Title", Interface = FieldInterface.Text)] public string? Title { get; set; }
    }

    [CmsCollection("Nn tr", Name = "nnTr")]
    public sealed class TranslatedCollection
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        [CmsTranslations(typeof(TranslatedCollectionTranslation))]
        [SugarColumn(IsIgnore = true)] public List<TranslatedCollectionTranslation> Translations { get; set; } = [];
    }

    public sealed class TranslatedCollectionTranslation
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        public long TranslatedCollectionId { get; set; }
        public string Locale { get; set; } = "";
        [SugarColumn(IsNullable = false)]
        [CmsField(Label = "Title", Interface = FieldInterface.Text)] public string? Title { get; set; }
    }

    [CmsCollection("Nn link", Name = "nnLink", Hidden = true)]
    public sealed class LinkCollection
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Left", Interface = FieldInterface.Uuid)] public Guid LeftId { get; set; }
        [CmsField(Label = "Right", Interface = FieldInterface.Uuid)] public Guid RightId { get; set; }
        [SugarColumn(IsNullable = false)]
        [CmsField(Label = "Rank", Interface = FieldInterface.Number)] public int? Rank { get; set; }
    }

    public static TheoryData<Type, string> Rejected() => new()
    {
        { typeof(GuidCollection), "GuidCollection.Owner" },
        { typeof(StringCollection), "StringCollection.Title" },
        { typeof(TranslatedCollection), "TranslatedCollectionTranslation.Title" },
        { typeof(LinkCollection), "LinkCollection.Rank" },
    };

    [Theory]
    [MemberData(nameof(Rejected))]
    public void A_nullable_property_that_declares_IsNullable_false_fails_the_scan(Type type, string property)
    {
        var act = () => MetadataScanner.ScanTypes([type]);

        var message = act.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain($"'{property}'")
            .And.Contain("nullable")
            .And.Contain("IsNullable = false")
            .And.Contain("non-nullable (Guid, not Guid?; string, not string?)")
            .And.Contain("remove IsNullable = false");
        message.All(c => c < 128).Should().BeTrue(message);
        message.Split(". ").Length.Should().BeLessThanOrEqualTo(2, message);
    }

    [CmsCollection("Nn allowed", Name = "nnAllowed")]
    public sealed class Allowed
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "A", Interface = FieldInterface.Uuid)] public Guid? PlainNullable { get; set; }
        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "B", Interface = FieldInterface.Uuid)] public Guid? ExplicitNullable { get; set; }
        [SugarColumn(Length = 50)]
        [CmsField(Label = "C", Interface = FieldInterface.Text)] public string? LengthOnly { get; set; }
        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "D", Interface = FieldInterface.Text)] public string NonNullableMadeNullable { get; set; } = "";
        [SugarColumn(IsNullable = false)]
        [CmsField(Label = "E", Interface = FieldInterface.Text)] public string NonNullableNotNull { get; set; } = "";
        [SugarColumn(IsNullable = false)]
        [CmsField(Label = "F", Interface = FieldInterface.Tags)] public List<string>? ListNotNull { get; set; }
        [SugarColumn(IsNullable = false)]
        [CmsField(Label = "G", Interface = FieldInterface.Number)] public int Count { get; set; }
    }

#nullable disable
    [CmsCollection("Nn oblivious", Name = "nnOblivious")]
    public sealed class Oblivious
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = false)]
        [CmsField(Label = "Title", Interface = FieldInterface.Text)] public string Title { get; set; }
    }
#nullable restore

    [Fact]
    public void Allowed_neighbours_scan()
    {
        var fields = MetadataScanner.ScanTypes([typeof(Allowed)]).Single().Fields;

        fields.Select(f => f.Name).Should().Contain(
            ["plainNullable", "explicitNullable", "lengthOnly", "nonNullableMadeNullable", "nonNullableNotNull", "listNotNull", "count"]);
    }

    [Fact]
    public void A_string_in_nullable_oblivious_code_may_declare_IsNullable_false()
    {
        MetadataScanner.ScanTypes([typeof(Oblivious)]).Single().Fields.Should().ContainSingle(f => f.Name == "title");
    }
}
