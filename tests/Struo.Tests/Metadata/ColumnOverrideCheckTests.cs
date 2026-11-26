using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// A property that overrides a base property carrying <c>[SugarColumn]</c> must repeat the attribute:
/// SqlSugar reads only the override's own declaration, while the scan would otherwise read the base's.
/// The scan refuses the override on a collection class, a translation class and a junction class.
/// </summary>
public sealed class ColumnOverrideCheckTests
{
    // ── bases: one virtual property each, with the attribute on the base only ──

    private abstract class IgnoreBase { [SugarColumn(IsIgnore = true)] public virtual string Note { get; set; } = ""; }
    private abstract class NullableBase { [SugarColumn(IsNullable = true)] public virtual string Note { get; set; } = ""; }
    private abstract class JsonBase { [SugarColumn(IsJson = true)] public virtual string Note { get; set; } = ""; }
    private abstract class DataTypeBase { [SugarColumn(ColumnDataType = "text")] public virtual string Note { get; set; } = ""; }
    private abstract class PlainBase { public virtual string Note { get; set; } = ""; }
    private abstract class GrandBase { [SugarColumn(IsNullable = true)] public virtual string Note { get; set; } = ""; }
    private abstract class MiddleBase : GrandBase { public string Other { get; set; } = ""; }
    private abstract class MiddleOverride : GrandBase { public override string Note { get; set; } = ""; }
    private abstract class PrimaryKeyBase { [SugarColumn(IsPrimaryKey = true)] public abstract Guid Id { get; set; } }

    // ── collection classes ──

    [CmsCollection("Coc ignore", Name = "cocIgnore")]
    private sealed class CollectionIgnore : IgnoreBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public override string Note { get; set; } = "";
    }

    [CmsCollection("Coc nullable", Name = "cocNullable")]
    private sealed class CollectionNullable : NullableBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public override string Note { get; set; } = "";
    }

    [CmsCollection("Coc json", Name = "cocJson")]
    private sealed class CollectionJson : JsonBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public override string Note { get; set; } = "";
    }

    [CmsCollection("Coc data type", Name = "cocDataType")]
    private sealed class CollectionDataType : DataTypeBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public override string Note { get; set; } = "";
    }

    [CmsCollection("Coc grand", Name = "cocGrand")]
    private sealed class CollectionTwoLevels : MiddleBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public override string Note { get; set; } = "";
    }

    [CmsCollection("Coc middle", Name = "cocMiddle")]
    private sealed class CollectionThroughMiddleOverride : MiddleOverride
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public override string Note { get; set; } = "";
    }

    [CmsCollection("Coc pk", Name = "cocPk")]
    private sealed class CollectionPrimaryKeyOverride : PrimaryKeyBase
    {
        public override Guid Id { get; set; }
    }

    // ── controls ──

    [CmsCollection("Coc repeats ignore", Name = "cocRepeatsIgnore")]
    private sealed class RepeatsIgnore : IgnoreBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Note", Interface = FieldInterface.Text)]
        [SugarColumn(IsIgnore = true)] public override string Note { get; set; } = "";
    }

    [CmsCollection("Coc repeats nullable", Name = "cocRepeatsNullable")]
    private sealed class RepeatsNullable : NullableBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Note", Interface = FieldInterface.Text)]
        [SugarColumn(IsNullable = true)] public override string Note { get; set; } = "";
    }

    [CmsCollection("Coc repeats data type", Name = "cocRepeatsDataType")]
    private sealed class RepeatsDataType : DataTypeBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Note", Interface = FieldInterface.Text)]
        [SugarColumn(ColumnDataType = "text")] public override string Note { get; set; } = "";
    }

    [CmsCollection("Coc hides", Name = "cocHides")]
    private sealed class HidesWithNew : NullableBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public new string Note { get; set; } = "";
    }

    [CmsCollection("Coc plain base", Name = "cocPlainBase")]
    private sealed class OverridesPlainBase : PlainBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public override string Note { get; set; } = "";
    }

    // ── translation classes ──

    [CmsCollection("Coc tr ignore", Name = "cocTrIgnore")]
    private sealed class TrIgnoreParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(TrIgnore))]
        [SugarColumn(IsIgnore = true)] public List<TrIgnore> Translations { get; set; } = [];
    }

    private sealed class TrIgnore : IgnoreBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid TrIgnoreParentId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public override string Note { get; set; } = "";
    }

    [CmsCollection("Coc tr nullable", Name = "cocTrNullable")]
    private sealed class TrNullableParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(TrNullable))]
        [SugarColumn(IsIgnore = true)] public List<TrNullable> Translations { get; set; } = [];
    }

    private sealed class TrNullable : NullableBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid TrNullableParentId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public override string Note { get; set; } = "";
    }

    [CmsCollection("Coc tr json", Name = "cocTrJson")]
    private sealed class TrJsonParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(TrJson))]
        [SugarColumn(IsIgnore = true)] public List<TrJson> Translations { get; set; } = [];
    }

    private sealed class TrJson : JsonBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid TrJsonParentId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public override string Note { get; set; } = "";
    }

    [CmsCollection("Coc tr repeats", Name = "cocTrRepeats")]
    private sealed class TrRepeatsParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(TrRepeats))]
        [SugarColumn(IsIgnore = true)] public List<TrRepeats> Translations { get; set; } = [];
    }

    private sealed class TrRepeats : NullableBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid TrRepeatsParentId { get; set; }
        public string Locale { get; set; } = "";
        [CmsField(Label = "Note", Interface = FieldInterface.Text)]
        [SugarColumn(IsNullable = true)] public override string Note { get; set; } = "";
    }

    // ── junction classes ──

    [CmsCollection("Coc target", Name = "cocTarget")]
    private sealed class JunctionTarget
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    private sealed class JunctionIgnore : IgnoreBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid TargetId { get; set; }
        public override string Note { get; set; } = "";
    }

    private sealed class JunctionNullable : NullableBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid TargetId { get; set; }
        public override string Note { get; set; } = "";
    }

    private sealed class JunctionJson : JsonBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid TargetId { get; set; }
        public override string Note { get; set; } = "";
    }

    private sealed class JunctionRepeats : NullableBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid TargetId { get; set; }
        [SugarColumn(IsNullable = true)] public override string Note { get; set; } = "";
    }

    [CmsCollection("Coc owner ignore", Name = "cocOwnerIgnore")]
    private sealed class OwnerOfIgnoreJunction
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [Navigate(typeof(JunctionIgnore), nameof(JunctionIgnore.OwnerId), nameof(JunctionIgnore.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<JunctionTarget> Tags { get; set; } = [];
    }

    [CmsCollection("Coc owner nullable", Name = "cocOwnerNullable")]
    private sealed class OwnerOfNullableJunction
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [Navigate(typeof(JunctionNullable), nameof(JunctionNullable.OwnerId), nameof(JunctionNullable.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<JunctionTarget> Tags { get; set; } = [];
    }

    [CmsCollection("Coc owner json", Name = "cocOwnerJson")]
    private sealed class OwnerOfJsonJunction
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [Navigate(typeof(JunctionJson), nameof(JunctionJson.OwnerId), nameof(JunctionJson.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<JunctionTarget> Tags { get; set; } = [];
    }

    [CmsCollection("Coc owner repeats", Name = "cocOwnerRepeats")]
    private sealed class OwnerOfRepeatingJunction
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [Navigate(typeof(JunctionRepeats), nameof(JunctionRepeats.OwnerId), nameof(JunctionRepeats.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<JunctionTarget> Tags { get; set; } = [];
    }

    // ── intermediate classes: only the properties the scanned type exposes are checked ──

    private abstract class RootColumn { [SugarColumn(IsNullable = true)] public virtual string X { get; set; } = ""; }
    private abstract class MiddleWithout : RootColumn { public override string X { get; set; } = ""; }
    private abstract class MiddleRepeats : RootColumn { [SugarColumn(IsNullable = true)] public override string X { get; set; } = ""; }

    [CmsCollection("Coc leaf repeats", Name = "cocLeafRepeats")]
    private sealed class LeafRepeatsOverMiddle : MiddleWithout
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public override string X { get; set; } = "";
    }

    [CmsCollection("Coc leaf hides", Name = "cocLeafHides")]
    private sealed class LeafHidesMiddle : MiddleWithout
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public new string X { get; set; } = "";
    }

    [CmsCollection("Coc leaf hides column", Name = "cocLeafHidesColumn")]
    private sealed class LeafHidesMiddleWithColumn : MiddleWithout
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public new string X { get; set; } = "";
    }

    private abstract class ProtectedRoot { [SugarColumn(IsNullable = true)] protected virtual string Hidden { get; set; } = ""; }
    private abstract class ProtectedMiddle : ProtectedRoot { protected override string Hidden { get; set; } = ""; }

    [CmsCollection("Coc protected", Name = "cocProtected")]
    private sealed class LeafOverBaseProtected : ProtectedMiddle
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [CmsCollection("Coc leaf without", Name = "cocLeafWithout")]
    private sealed class LeafWithoutOverMiddleRepeats : MiddleRepeats
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public override string X { get; set; } = "";
    }

    [CmsCollection("Coc sibling", Name = "cocSibling")]
    private sealed class SiblingInheritingMiddle : MiddleWithout
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    private abstract class GenericBase<T> { [SugarColumn(IsNullable = true)] public virtual T? Note { get; set; } }

    [CmsCollection("Coc generic", Name = "cocGeneric")]
    private sealed class OverridesGenericBase : GenericBase<string>
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public override string? Note { get; set; }
    }

    [CmsCollection("Coc get only", Name = "cocGetOnly")]
    private sealed class GetOnlyOverride : NullableBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public override string Note => "";
    }

    private sealed class TrKeyOverride : PrimaryKeyBase
    {
        public override Guid Id { get; set; }
        public Guid TrKeyParentId { get; set; }
        public string Locale { get; set; } = "";
    }

    [CmsCollection("Coc tr key", Name = "cocTrKey")]
    private sealed class TrKeyParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsTranslations(typeof(TrKeyOverride))]
        [SugarColumn(IsIgnore = true)] public List<TrKeyOverride> Translations { get; set; } = [];
    }

    [CmsCollection("Coc other key", Name = "cocOtherKey")]
    private sealed class OtherMarkedKey : PrimaryKeyBase
    {
        public override Guid Id { get; set; }
        [SugarColumn(IsPrimaryKey = true)] public Guid Code { get; set; }
    }

    private abstract class ColumnAndFieldBase
    {
        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Note", Interface = FieldInterface.Text)] public virtual string Note { get; set; } = "";
    }

    [CmsCollection("Coc both dropped", Name = "cocBothDropped")]
    private sealed class DropsColumnAndField : ColumnAndFieldBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public override string Note { get; set; } = "";
    }

    // ── tests ──

    public static TheoryData<Type[], string, string> Refused() => new()
    {
        { [typeof(LeafWithoutOverMiddleRepeats)], "LeafWithoutOverMiddleRepeats.X", "MiddleRepeats.X" },
        { [typeof(OverridesGenericBase)], "OverridesGenericBase.Note", "GenericBase`1.Note" },
        { [typeof(GetOnlyOverride)], "GetOnlyOverride.Note", "NullableBase.Note" },
        { [typeof(TrKeyParent)], "TrKeyOverride.Id", "PrimaryKeyBase.Id" },
        { [typeof(OtherMarkedKey)], "OtherMarkedKey.Id", "PrimaryKeyBase.Id" },
        { [typeof(DropsColumnAndField)], "DropsColumnAndField.Note", "ColumnAndFieldBase.Note" },
        { [typeof(CollectionIgnore)], "CollectionIgnore.Note", "IgnoreBase.Note" },
        { [typeof(CollectionNullable)], "CollectionNullable.Note", "NullableBase.Note" },
        { [typeof(CollectionJson)], "CollectionJson.Note", "JsonBase.Note" },
        { [typeof(CollectionDataType)], "CollectionDataType.Note", "DataTypeBase.Note" },
        { [typeof(CollectionTwoLevels)], "CollectionTwoLevels.Note", "GrandBase.Note" },
        { [typeof(CollectionThroughMiddleOverride)], "CollectionThroughMiddleOverride.Note", "GrandBase.Note" },
        { [typeof(TrIgnoreParent)], "TrIgnore.Note", "IgnoreBase.Note" },
        { [typeof(TrNullableParent)], "TrNullable.Note", "NullableBase.Note" },
        { [typeof(TrJsonParent)], "TrJson.Note", "JsonBase.Note" },
        { [typeof(OwnerOfIgnoreJunction), typeof(JunctionTarget)], "JunctionIgnore.Note", "IgnoreBase.Note" },
        { [typeof(OwnerOfNullableJunction), typeof(JunctionTarget)], "JunctionNullable.Note", "NullableBase.Note" },
        { [typeof(OwnerOfJsonJunction), typeof(JunctionTarget)], "JunctionJson.Note", "JsonBase.Note" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void An_override_that_does_not_repeat_SugarColumn_is_refused_and_names_both_declarations(
        Type[] types, string overriding, string overridden)
    {
        var scan = () => MetadataScanner.ScanTypes(types);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith($"Property '{overriding}' overrides '{overridden}', which carries [SugarColumn], without repeating it.")
            .And.Contain("SqlSugar reads only the override's own [SugarColumn]")
            .And.EndWith($"Repeat [SugarColumn(...)] on '{overriding}'.");
    }

    public static TheoryData<Type[]> Accepted() => new()
    {
        { [typeof(RepeatsIgnore)] },
        { [typeof(RepeatsNullable)] },
        { [typeof(RepeatsDataType)] },
        { [typeof(HidesWithNew)] },
        { [typeof(OverridesPlainBase)] },
        { [typeof(TrRepeatsParent)] },
        { [typeof(OwnerOfRepeatingJunction), typeof(JunctionTarget)] },
        { [typeof(LeafRepeatsOverMiddle)] },
        { [typeof(LeafHidesMiddle)] },
        { [typeof(LeafHidesMiddleWithColumn)] },
        { [typeof(LeafOverBaseProtected)] },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void A_declaration_that_repeats_the_attribute_or_has_none_to_repeat_is_accepted(Type[] types)
    {
        var scan = () => MetadataScanner.ScanTypes(types);

        scan.Should().NotThrow();
    }

    [Fact]
    public void A_collection_that_inherits_an_override_without_the_attribute_is_refused_and_named_as_inheriting_it()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(SiblingInheritingMiddle)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith("Property 'MiddleWithout.X' overrides 'RootColumn.X', which carries [SugarColumn], without repeating it.")
            .And.EndWith("Repeat [SugarColumn(...)] on 'MiddleWithout.X', which 'SiblingInheritingMiddle' inherits.");
    }

    [Fact]
    public void An_override_that_drops_both_attributes_reports_SugarColumn_first()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(DropsColumnAndField)]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().Contain("which carries [SugarColumn]")
            .And.NotContain("[CmsField]");
    }

    [Fact]
    public void An_override_of_a_primary_key_keeps_the_primary_key_message()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(CollectionPrimaryKeyOverride)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("has no primary key property")
            .And.Contain("an override of 'Id' must repeat [SugarColumn(IsPrimaryKey = true)].")
            .And.NotContain("without repeating it");
    }
}
