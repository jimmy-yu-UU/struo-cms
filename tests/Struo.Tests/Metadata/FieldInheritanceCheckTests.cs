using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// <c>[CmsField]</c> is not inherited: a member that overrides or hides a base <c>[CmsField]</c> property
/// without carrying the attribute itself leaves the field unserved. The scan refuses that on a collection
/// class, a translation class and a repeater child type.
/// </summary>
public sealed class FieldInheritanceCheckTests
{
    private abstract class FieldBase
    {
        [CmsField(Label = "Title", Interface = FieldInterface.Text)] public virtual string Title { get; set; } = "";
        [CmsField(Label = "X", Interface = FieldInterface.Text)] public string X { get; set; } = "";
    }

    // ── collection classes ──

    [CmsCollection("Fic override", Name = "ficOverride")]
    private sealed class CollectionOverride : FieldBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public override string Title { get; set; } = "";
    }

    [CmsCollection("Fic internal", Name = "ficInternal")]
    private sealed class CollectionHiddenInternal : FieldBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        internal new string X { get; set; } = "";
    }

    [CmsCollection("Fic public", Name = "ficPublic")]
    private sealed class CollectionHiddenPublic : FieldBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public new string X { get; set; } = "";
    }

    [CmsCollection("Fic repeats", Name = "ficRepeats")]
    private sealed class CollectionRepeats : FieldBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Title", Interface = FieldInterface.Text)] public override string Title { get; set; } = "";
    }

    [CmsCollection("Fic hides with field", Name = "ficHidesWithField")]
    private sealed class CollectionHidesWithField : FieldBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "X", Interface = FieldInterface.Text)] public new string X { get; set; } = "";
    }

    // ── translation classes ──

    [CmsCollection("Fic tr override", Name = "ficTrOverride")]
    private sealed class TrOverrideParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(TrOverride))]
        [SugarColumn(IsIgnore = true)] public List<TrOverride> Translations { get; set; } = [];
    }

    private sealed class TrOverride : FieldBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid TrOverrideParentId { get; set; }
        public string Locale { get; set; } = "";
        public override string Title { get; set; } = "";
    }

    [CmsCollection("Fic tr internal", Name = "ficTrInternal")]
    private sealed class TrInternalParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(TrInternal))]
        [SugarColumn(IsIgnore = true)] public List<TrInternal> Translations { get; set; } = [];
    }

    private sealed class TrInternal : FieldBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid TrInternalParentId { get; set; }
        public string Locale { get; set; } = "";
        internal new string X { get; set; } = "";
    }

    [CmsCollection("Fic tr public", Name = "ficTrPublic")]
    private sealed class TrPublicParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(TrPublic))]
        [SugarColumn(IsIgnore = true)] public List<TrPublic> Translations { get; set; } = [];
    }

    private sealed class TrPublic : FieldBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid TrPublicParentId { get; set; }
        public string Locale { get; set; } = "";
        public new string X { get; set; } = "";
    }

    [CmsCollection("Fic tr repeats", Name = "ficTrRepeats")]
    private sealed class TrRepeatsParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(TrRepeats))]
        [SugarColumn(IsIgnore = true)] public List<TrRepeats> Translations { get; set; } = [];
    }

    private sealed class TrRepeats : FieldBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid TrRepeatsParentId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Label = "X", Interface = FieldInterface.Text)] public new string X { get; set; } = "";
    }

    // ── repeater child types ──

    private sealed class ChildOverride : FieldBase { public override string Title { get; set; } = ""; }
    private sealed class ChildInternal : FieldBase { internal new string X { get; set; } = ""; }
    private sealed class ChildPublic : FieldBase { public new string X { get; set; } = ""; }

    private sealed class ChildRepeats : FieldBase
    {
        [CmsField(Label = "Title", Interface = FieldInterface.Text)] public override string Title { get; set; } = "";
    }

    [CmsCollection("Fic rep override", Name = "ficRepOverride")]
    private sealed class RepeaterOverride
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<ChildOverride> Rows { get; set; } = [];
    }

    [CmsCollection("Fic rep internal", Name = "ficRepInternal")]
    private sealed class RepeaterInternal
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<ChildInternal> Rows { get; set; } = [];
    }

    [CmsCollection("Fic rep public", Name = "ficRepPublic")]
    private sealed class RepeaterPublic
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<ChildPublic> Rows { get; set; } = [];
    }

    [CmsCollection("Fic rep repeats", Name = "ficRepRepeats")]
    private sealed class RepeaterRepeats
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Rows", Interface = FieldInterface.Repeater)] public List<ChildRepeats> Rows { get; set; } = [];
    }

    // ── a deeper hierarchy ──

    private abstract class AbstractRoot
    {
        [CmsField(Label = "Y", Interface = FieldInterface.Text)] public abstract string Y { get; set; }
    }

    private abstract class MiddleRepeatsField : AbstractRoot
    {
        [CmsField(Label = "Y", Interface = FieldInterface.Text)] public override string Y { get; set; } = "";
    }

    private abstract class MiddleWithoutField : AbstractRoot
    {
        public override string Y { get; set; } = "";
    }

    [CmsCollection("Fic leaf drops", Name = "ficLeafDrops")]
    private sealed class LeafDropsField : MiddleRepeatsField
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public override string Y { get; set; } = "";
    }

    [CmsCollection("Fic leaf repeats", Name = "ficLeafRepeats")]
    private sealed class LeafRepeatsField : MiddleWithoutField
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Y", Interface = FieldInterface.Text)] public override string Y { get; set; } = "";
    }

    // ── tests ──

    [Fact]
    public void A_leaf_override_without_CmsField_names_the_leaf_and_the_nearest_declaration_that_carries_it()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(LeafDropsField)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().StartWith("Field 'LeafDropsField.Y' overrides 'MiddleRepeatsField.Y', which carries [CmsField], without repeating it.");
    }

    [Fact]
    public void A_leaf_that_repeats_CmsField_over_a_middle_class_without_it_is_accepted()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(LeafRepeatsField)]);

        scan.Should().NotThrow();
    }

    public static TheoryData<Type, string> RefusedOverrides() => new()
    {
        { typeof(CollectionOverride), "CollectionOverride.Title" },
        { typeof(TrOverrideParent), "TrOverride.Title" },
        { typeof(RepeaterOverride), "ChildOverride.Title" },
    };

    [Theory]
    [MemberData(nameof(RefusedOverrides))]
    public void An_override_that_does_not_repeat_CmsField_is_refused(Type owner, string overriding)
    {
        var scan = () => MetadataScanner.ScanTypes([owner]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith($"Field '{overriding}' overrides 'FieldBase.Title', which carries [CmsField], without repeating it.")
            .And.Contain("[CmsField] is not inherited, so the field is not served.")
            .And.EndWith($"Repeat [CmsField(...)] on '{overriding}', or remove it from 'FieldBase.Title'.");
    }

    public static TheoryData<Type, string> RefusedHidings() => new()
    {
        { typeof(CollectionHiddenInternal), "CollectionHiddenInternal.X" },
        { typeof(CollectionHiddenPublic), "CollectionHiddenPublic.X" },
        { typeof(TrInternalParent), "TrInternal.X" },
        { typeof(TrPublicParent), "TrPublic.X" },
        { typeof(RepeaterInternal), "ChildInternal.X" },
        { typeof(RepeaterPublic), "ChildPublic.X" },
    };

    [Theory]
    [MemberData(nameof(RefusedHidings))]
    public void A_member_that_hides_a_CmsField_property_without_carrying_it_is_refused(Type owner, string hiding)
    {
        var scan = () => MetadataScanner.ScanTypes([owner]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith($"Field 'FieldBase.X' is hidden by '{hiding}', which does not carry [CmsField], so the field is not served.")
            .And.EndWith($"Put [CmsField(...)] on '{hiding}' and declare it public, or remove the hiding member.");
    }

    public static TheoryData<Type> Accepted() => new()
    {
        { typeof(CollectionRepeats) },
        { typeof(CollectionHidesWithField) },
        { typeof(TrRepeatsParent) },
        { typeof(RepeaterRepeats) },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void A_member_that_carries_CmsField_itself_is_accepted(Type owner)
    {
        var scan = () => MetadataScanner.ScanTypes([owner]);

        scan.Should().NotThrow();
    }
}
