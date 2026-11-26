using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// A <c>[CmsField]</c> on a property the scan never reads (static, or without a public accessor) is
/// refused at the scan, whichever type declares it: a collection class, a base class of one, a translation
/// class or a repeater child type.
/// </summary>
public sealed class FieldDeclarationCheckTests
{
    [CmsCollection("Fdc private", Name = "fdcPrivate")]
    private sealed class PrivateField
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Hidden", Interface = FieldInterface.Text)] private string Hidden { get; set; } = "";
        public string Peek() => Hidden;
    }

    [CmsCollection("Fdc internal", Name = "fdcInternal")]
    private sealed class InternalField
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Hidden", Interface = FieldInterface.Text)] internal string Hidden { get; set; } = "";
    }

    [CmsCollection("Fdc protected", Name = "fdcProtected")]
    private class ProtectedField
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Hidden", Interface = FieldInterface.Text)] protected string Hidden { get; set; } = "";
    }

    [CmsCollection("Fdc static", Name = "fdcStatic")]
    private sealed class StaticField
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Shared", Interface = FieldInterface.Text)] public static string Shared { get; set; } = "";
    }

    private abstract class BaseWithPrivateField
    {
        [CmsField(Label = "Hidden", Interface = FieldInterface.Text)] private string Hidden { get; set; } = "";
        public string Peek() => Hidden;
    }

    [CmsCollection("Fdc base", Name = "fdcBase")]
    private sealed class SubclassOfAPrivateField : BaseWithPrivateField
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [CmsCollection("Fdc translated", Name = "fdcTranslated")]
    private sealed class TranslatedCollection
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(PrivateFieldTranslation))]
        [SugarColumn(IsIgnore = true)] public List<PrivateFieldTranslation> Translations { get; set; } = [];
    }

    private sealed class PrivateFieldTranslation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid TranslatedCollectionId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Label = "Title", Interface = FieldInterface.Text)] public string Title { get; set; } = "";
        [CmsField(Label = "Hidden", Interface = FieldInterface.Text)] internal string Hidden { get; set; } = "";
    }

    private sealed class StaticFieldChild
    {
        [CmsField(Label = "Title", Interface = FieldInterface.Text)] public string Title { get; set; } = "";
        [CmsField(Label = "Shared", Interface = FieldInterface.Text)] public static string Shared { get; set; } = "";
    }

    [CmsCollection("Fdc repeater", Name = "fdcRepeater")]
    private sealed class RepeaterOfStaticField
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<StaticFieldChild> Rows { get; set; } = [];
    }

    [CmsCollection("Fdc accessors", Name = "fdcAccessors")]
    private sealed class PublicWithUnusualAccessors
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Init", Interface = FieldInterface.Text)] public string Init { get; init; } = "";
        [CmsField(Label = "Private get", Interface = FieldInterface.Text)] public string PrivateGet { private get; set; } = "";
        [CmsField(Label = "Private set", Interface = FieldInterface.Text, ReadOnly = true)] public string PrivateSet { get; private set; } = "";
        [CmsField(Label = "Get only", Interface = FieldInterface.Text, ReadOnly = true)] public string GetOnly { get; } = "";
    }

    public static TheoryData<Type, string, string> MisplacedFields() => new()
    {
        { typeof(PrivateField), "PrivateField.Hidden", "not public" },
        { typeof(InternalField), "InternalField.Hidden", "not public" },
        { typeof(ProtectedField), "ProtectedField.Hidden", "not public" },
        { typeof(StaticField), "StaticField.Shared", "static" },
        { typeof(SubclassOfAPrivateField), "BaseWithPrivateField.Hidden", "not public" },
        { typeof(TranslatedCollection), "PrivateFieldTranslation.Hidden", "not public" },
        { typeof(RepeaterOfStaticField), "StaticFieldChild.Shared", "static" },
    };

    [Theory]
    [MemberData(nameof(MisplacedFields))]
    public void A_field_that_is_not_a_public_instance_property_is_refused_and_names_what_it_is(
        Type owner, string field, string what)
    {
        var scan = () => MetadataScanner.ScanTypes([owner]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith($"Field '{field}' has [CmsField] but is {what}")
            .And.Contain("the field is not served")
            .And.Contain("declare it public and without static, or remove [CmsField]");
    }

    [Fact]
    public void A_public_property_whose_accessors_are_restricted_is_still_a_field()
    {
        var fields = MetadataScanner.ScanTypes([typeof(PublicWithUnusualAccessors)]).Single().Fields;

        fields.Select(f => f.Name).Should().Contain(["init", "privateGet", "privateSet", "getOnly"]);
    }
}
