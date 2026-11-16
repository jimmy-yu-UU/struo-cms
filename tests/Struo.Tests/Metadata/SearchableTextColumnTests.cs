// tests/Struo.Tests/Metadata/SearchableTextColumnTests.cs
using System.Reflection;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Metadata;

// Search matches text with a text operator, so Searchable on a column that is not text fails the scan.
public sealed class SearchableTextColumnTests
{
    [CmsCollection("Search on a number", Name = "stcNumber")]
    public sealed class SearchableNumber
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Number, Searchable = true)] public int Count { get; set; }
    }

    [CmsCollection("Search on a Guid", Name = "stcGuid")]
    public sealed class SearchableGuid
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Uuid, Searchable = true)] public Guid Ref { get; set; }
    }

    [CmsCollection("Search on a date", Name = "stcDate")]
    public sealed class SearchableDate
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.DateTime, Searchable = true)] public DateTime? At { get; set; }
    }

    [CmsCollection("Search on a boolean", Name = "stcBool")]
    public sealed class SearchableBool
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Boolean, Searchable = true)] public bool On { get; set; }
    }

    [CmsCollection("Search on translated number", Name = "stcTranslated")]
    public sealed class TranslatedHost
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(TranslatedNumberText))]
        [SugarColumn(IsIgnore = true)]
        public List<TranslatedNumberText> Translations { get; set; } = [];
    }

    public sealed class TranslatedNumberText
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        public Guid TranslatedHostId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Interface = FieldInterface.Number, Searchable = true)] public int Rank { get; set; }
    }

    [CmsCollection("Search on text-like columns", Name = "stcText")]
    public sealed class SearchableText
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Text, Searchable = true)] public string Name { get; set; } = "";
        [CmsField(Interface = FieldInterface.Email, Searchable = true)] public string? Mail { get; set; }
        [CmsField(Interface = FieldInterface.Tags, Searchable = true)] public List<TagItem> Labels { get; set; } = [];
        [CmsField(Interface = FieldInterface.KeyValue, Searchable = true)] public Dictionary<string, string> Meta { get; set; } = new();
        [CmsField(Interface = FieldInterface.Hidden, Searchable = true)] public List<int> Numbers { get; set; } = [];
    }

    [CmsCollection("Search on a repeater sub-field", Name = "stcRepeater")]
    public sealed class SearchableSubField
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Repeater)] public List<SearchableItem> Items { get; set; } = [];
    }

    public sealed class SearchableItem
    {
        [CmsField(Interface = FieldInterface.Text, Searchable = true)] public string Question { get; set; } = "";
    }

    [CmsCollection("Searchable structured and hidden fields", Name = "stcHidden")]
    public sealed class HiddenSearchables
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        // A structured type on the Hidden interface is stored as JSON, so it is a text column.
        [CmsField(Interface = FieldInterface.Hidden, Searchable = true)] public List<string> Labels { get; set; } = [];
    }

    [CmsCollection("Searchable text marked Hidden", Name = "stcHiddenText")]
    public sealed class HiddenFlagText
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Text, Hidden = true, Searchable = true)] public string Secret { get; set; } = "";
    }

    [CmsCollection("Searchable number marked Hidden", Name = "stcHiddenNumberFlag")]
    public sealed class HiddenFlagNumber
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Number, Hidden = true, Searchable = true)] public int Secret { get; set; }
    }

    [CmsCollection("Searchable number on the Hidden interface", Name = "stcHiddenNumber")]
    public sealed class HiddenInterfaceNumber
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Hidden, Searchable = true)] public int Count { get; set; }
    }

    [CmsCollection("Number that is not searchable", Name = "stcQuiet")]
    public sealed class QuietNumber
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Interface = FieldInterface.Number)] public int Count { get; set; }
    }

    [Theory]
    [InlineData(typeof(SearchableNumber), "SearchableNumber.Count", "int")]
    [InlineData(typeof(SearchableGuid), "SearchableGuid.Ref", "Guid")]
    [InlineData(typeof(SearchableDate), "SearchableDate.At", "DateTime?")]
    [InlineData(typeof(SearchableBool), "SearchableBool.On", "bool")]
    [InlineData(typeof(TranslatedHost), "TranslatedNumberText.Rank", "int")]
    public void Searchable_on_a_column_that_is_not_text_fails_the_scan_and_says_what_to_change(
        Type type, string field, string columnType)
    {
        var act = () => MetadataScanner.ScanTypes([type]);
        var message = act.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain(field).And.Contain("Searchable").And.Contain("text only")
            .And.Contain(columnType).And.Contain("Remove Searchable");
    }

    [Fact]
    public void Searchable_on_a_repeater_sub_field_fails_the_scan_and_says_so()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(SearchableSubField)]);
        act.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("SearchableItem.Question").And.Contain("Repeater sub-field")
            .And.Contain("never searched").And.Contain("Remove Searchable");
    }

    [Fact]
    public void Searchable_on_text_and_JSON_stored_columns_scans()
    {
        var meta = MetadataScanner.ScanTypes([typeof(SearchableText)]).Single();
        meta.Fields.Where(f => f.Searchable).Select(f => f.Name)
            .Should().BeEquivalentTo(["name", "mail", "labels", "meta", "numbers"]);
    }

    [Fact]
    public void A_structured_property_on_the_Hidden_interface_scans_with_Searchable()
    {
        var meta = MetadataScanner.ScanTypes([typeof(HiddenSearchables)]).Single();
        meta.Fields.Where(f => f.Searchable).Select(f => f.Name).Should().BeEquivalentTo(["labels"]);
    }

    // A field marked Hidden is dropped from the search whitelist, so Searchable on it does nothing.
    [Theory]
    [InlineData(typeof(HiddenFlagText), "HiddenFlagText.Secret")]
    [InlineData(typeof(HiddenFlagNumber), "HiddenFlagNumber.Secret")]
    public void Searchable_together_with_Hidden_fails_the_scan_whatever_the_property_type(Type type, string field)
    {
        var act = () => MetadataScanner.ScanTypes([type]);
        act.Should().Throw<MetadataException>().Which.Message
            .Should().Contain(field).And.Contain("never searched").And.Contain("Remove Hidden = true or Searchable = true");
    }

    [Fact]
    public void A_number_on_the_Hidden_interface_is_not_text_and_fails_with_Searchable()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(HiddenInterfaceNumber)]);
        act.Should().Throw<MetadataException>().Which.Message.Should().Contain("HiddenInterfaceNumber.Count").And.Contain("int");
    }

    [Theory]
    [InlineData(typeof(List<string>), "List<string>")]
    [InlineData(typeof(Dictionary<string, int>), "Dictionary<string, int>")]
    [InlineData(typeof(DateTime?), "DateTime?")]
    [InlineData(typeof(Guid), "Guid")]
    [InlineData(typeof(int), "int")]
    public void The_type_label_reads_like_the_declaration(Type type, string label)
    {
        MetadataScanner.TypeLabel(type).Should().Be(label);
    }

    [Fact]
    public void A_number_that_is_not_searchable_scans()
    {
        MetadataScanner.ScanTypes([typeof(QuietNumber)]).Should().ContainSingle();
    }

    [Fact]
    public void The_core_and_fixture_collections_scan_with_their_searchable_fields()
    {
        var core = MetadataScanner.ScanTypes(FrameworkEntityTypes.All);
        var fixtures = MetadataScanner.Scan(typeof(FxArticle).Assembly);
        core.Concat(fixtures).SelectMany(c => c.Fields).Where(f => f.Searchable).Should().NotBeEmpty();
    }
}

/// <summary>
/// The scanner decides "text" from the entity type and the field interface; the query translator decides
/// it from the ORM's column info. Every field of the core and fixture collections gets the same answer
/// from both.
/// </summary>
public sealed class TextColumnAgreementTests : IDisposable
{
    private readonly ITestDatabase _database = TestBackend.CreateUnitDatabase();

    public void Dispose() => _database.Dispose();

    private static IEnumerable<Type> CollectionTypes() =>
        FrameworkEntityTypes.All.Concat(MetadataScanner.LoadTypes(typeof(FxArticle).Assembly));

    private static EntityDescriptor DescriptorOf(Type type) =>
        new(type, type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..], p => p.Name, StringComparer.OrdinalIgnoreCase), "Id");

    [Fact]
    public void Every_field_of_the_core_and_fixture_collections_is_text_on_both_sides_or_on_neither()
    {
        var db = SqlSugarClientFactory.Create(_database.Options(), new TestCurrentUserAccessor(Guid.Empty));
        var types = CollectionTypes().ToList();
        var checkedFields = 0;
        var textFields = 0;
        var compared = new HashSet<string>();

        foreach (var collection in MetadataScanner.ScanTypes(types))
        {
            var entity = types.First(t => MetadataScanner.CollectionNameOf(t) == collection.Name);
            foreach (var field in collection.Fields)
            {
                var owner = field.Translatable && collection.Translation is { } t ? t.TranslationEntityType : entity;
                var prop = owner.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .FirstOrDefault(p => string.Equals(p.Name, field.Name, StringComparison.OrdinalIgnoreCase));
                if (prop is null) continue;

                var scannerSide = MetadataScanner.IsTextColumn(prop, field.Interface);
                var querySide = ConditionalModelTranslator.Target(field.Name, DescriptorOf(owner), db).IsText;
                querySide.Should().Be(scannerSide, $"{collection.Name}.{field.Name} ({prop.PropertyType.Name}, {field.Interface})");
                checkedFields++;
                compared.Add($"{collection.Name}.{field.Name}");
                if (scannerSide) textFields++;
            }
        }

        // The Hidden structured fields, and the one on a translation entity, are compared too.
        compared.Should().Contain(["fxStash.labels", "fxStash.numbers", "fxStash.scores", "fxStash.note", "fxGlossary.aliases"]);
        checkedFields.Should().BeGreaterThan(40);
        textFields.Should().BeGreaterThan(10).And.BeLessThan(checkedFields);
    }
}
