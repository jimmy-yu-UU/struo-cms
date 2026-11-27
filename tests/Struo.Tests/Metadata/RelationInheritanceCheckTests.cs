using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// <c>[CmsRelation]</c> is not inherited: a member that overrides or hides a base relation property without
/// carrying the attribute itself leaves the relation unserved, whatever it does with <c>[Navigate]</c>. The
/// scan refuses that on a collection class.
/// </summary>
public sealed class RelationInheritanceCheckTests
{
    private const string NotServed = "the schema does not list it, a read does not expand it and a write does not link it";

    [CmsCollection("Ric target", Name = "ricTarget")]
    private sealed class Target
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    }

    private abstract class RelationBase
    {
        public Guid? TargetId { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
        [SugarColumn(IsIgnore = true)]
        public virtual Target? Link { get; set; }
    }

    // ── overrides ──

    [CmsCollection("Ric override neither", Name = "ricOverrideNeither")]
    private sealed class OverrideNeither : RelationBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsIgnore = true)] public override Target? Link { get; set; }
    }

    [CmsCollection("Ric override navigate", Name = "ricOverrideNavigate")]
    private sealed class OverrideNavigateOnly : RelationBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [SugarColumn(IsIgnore = true)] public override Target? Link { get; set; }
    }

    [CmsCollection("Ric override relation", Name = "ricOverrideRelation")]
    private sealed class OverrideRelationOnly : RelationBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
        [SugarColumn(IsIgnore = true)] public override Target? Link { get; set; }
    }

    [CmsCollection("Ric override both", Name = "ricOverrideBoth")]
    private sealed class OverrideBoth : RelationBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
        [SugarColumn(IsIgnore = true)] public override Target? Link { get; set; }
    }

    // ── hiding members ──

    [CmsCollection("Ric hide neither", Name = "ricHideNeither")]
    private sealed class HideNeither : RelationBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsIgnore = true)] public new Target? Link { get; set; }
    }

    [CmsCollection("Ric hide navigate", Name = "ricHideNavigate")]
    private sealed class HideNavigateOnly : RelationBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [SugarColumn(IsIgnore = true)] public new Target? Link { get; set; }
    }

    [CmsCollection("Ric hide relation", Name = "ricHideRelation")]
    private sealed class HideRelationOnly : RelationBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
        [SugarColumn(IsIgnore = true)] public new Target? Link { get; set; }
    }

    [CmsCollection("Ric hide both", Name = "ricHideBoth")]
    private sealed class HideBoth : RelationBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
        [SugarColumn(IsIgnore = true)] public new Target? Link { get; set; }
    }

    // ── a class that inherits the declaration unchanged, and a chain ──

    [CmsCollection("Ric inherits", Name = "ricInherits")]
    private sealed class Inherits : RelationBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    private abstract class MiddleWithoutRelation : RelationBase
    {
        [SugarColumn(IsIgnore = true)] public override Target? Link { get; set; }
    }

    [CmsCollection("Ric chain leaf repeats", Name = "ricChainLeafRepeats")]
    private sealed class ChainLeafRepeats : MiddleWithoutRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
        [SugarColumn(IsIgnore = true)] public override Target? Link { get; set; }
    }

    [CmsCollection("Ric chain leaf omits", Name = "ricChainLeafOmits")]
    private sealed class ChainLeafOmits : MiddleWithoutRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    // ── abstract declarations and interface members ──

    private abstract class AbstractRoot
    {
        public Guid? TargetId { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
        [SugarColumn(IsIgnore = true)]
        public abstract Target? Link { get; set; }
    }

    [CmsCollection("Ric abstract omits", Name = "ricAbstractOmits")]
    private sealed class AbstractOmits : AbstractRoot
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsIgnore = true)] public override Target? Link { get; set; }
    }

    [CmsCollection("Ric abstract repeats", Name = "ricAbstractRepeats")]
    private sealed class AbstractRepeats : AbstractRoot
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
        [SugarColumn(IsIgnore = true)] public override Target? Link { get; set; }
    }

    private interface IHasLink
    {
        [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
        Target? Link { get; set; }
    }

    [CmsCollection("Ric interface", Name = "ricInterface")]
    private sealed class ImplementsInterface : IHasLink
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsIgnore = true)] public Target? Link { get; set; }
    }

    // ── bare overrides, middle classes, generic base, different arguments ──

    [CmsCollection("Ric bare override", Name = "ricBareOverride")]
    private sealed class BareOverride : RelationBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public override Target? Link { get; set; }
    }

    [CmsCollection("Ric bare hide", Name = "ricBareHide")]
    private sealed class BareHide : RelationBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public new Target? Link { get; set; }
    }

    [CmsCollection("Ric override no column", Name = "ricOverrideNoColumn")]
    private sealed class OverrideWithoutColumn : RelationBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
        public override Target? Link { get; set; }
    }

    private abstract class MiddleHides : RelationBase
    {
        [SugarColumn(IsIgnore = true)] public new Target? Link { get; set; }
    }

    [CmsCollection("Ric middle hides", Name = "ricMiddleHides")]
    private sealed class LeafOfHidingMiddle : MiddleHides
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    private abstract class MiddleRepeats : RelationBase
    {
        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
        [SugarColumn(IsIgnore = true)]
        public override Target? Link { get; set; }
    }

    [CmsCollection("Ric leaf omits", Name = "ricLeafOmitsAfterRepeat")]
    private sealed class LeafOmitsAfterRepeat : MiddleRepeats
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsIgnore = true)] public override Target? Link { get; set; }
    }

    private abstract class GenericBase<T> where T : class
    {
        public Guid? TargetId { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
        [SugarColumn(IsIgnore = true)]
        public virtual T? Link { get; set; }
    }

    [CmsCollection("Ric generic omits", Name = "ricGenericOmits")]
    private sealed class GenericOmits : GenericBase<Target>
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsIgnore = true)] public override Target? Link { get; set; }
    }

    [CmsCollection("Ric generic repeats", Name = "ricGenericRepeats")]
    private sealed class GenericRepeats : GenericBase<Target>
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name} (derived)")]
        [SugarColumn(IsIgnore = true)]
        public override Target? Link { get; set; }
    }

    // ── tests ──

    private static string OverrideMessage(string derived) =>
        $"Relation '{derived}.Link' overrides 'RelationBase.Link', which carries [CmsRelation], without repeating it. " +
        $"[CmsRelation] is not inherited, so the relation is not served: {NotServed}. " +
        $"Repeat [CmsRelation(...)], [Navigate(...)] and [SugarColumn(IsIgnore = true)] on '{derived}.Link', or remove [CmsRelation] and [Navigate] from 'RelationBase.Link'.";

    private static string HidingMessage(string derived) =>
        $"Relation 'RelationBase.Link' is hidden by '{derived}.Link', which does not carry [CmsRelation], so the " +
        $"relation is not served: {NotServed}. " +
        $"Put [CmsRelation(...)], [Navigate(...)] and [SugarColumn(IsIgnore = true)] on '{derived}.Link' and declare it public, or remove the hiding member.";

    public static TheoryData<Type, string> RefusedOverrides() => new()
    {
        { typeof(OverrideNeither), OverrideMessage("OverrideNeither") },
        { typeof(OverrideNavigateOnly), OverrideMessage("OverrideNavigateOnly") },
        { typeof(ChainLeafOmits), OverrideMessage("MiddleWithoutRelation") },
        { typeof(BareOverride), OverrideMessage("BareOverride") },
    };

    [Theory]
    [MemberData(nameof(RefusedOverrides))]
    public void An_override_without_CmsRelation_is_refused(Type collection, string message)
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(Target), collection]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().Be(message);
    }

    public static TheoryData<Type, string> RefusedHidings() => new()
    {
        { typeof(HideNeither), HidingMessage("HideNeither") },
        { typeof(HideNavigateOnly), HidingMessage("HideNavigateOnly") },
        { typeof(BareHide), HidingMessage("BareHide") },
        { typeof(LeafOfHidingMiddle), HidingMessage("MiddleHides") },
    };

    [Theory]
    [MemberData(nameof(RefusedHidings))]
    public void A_hiding_member_without_CmsRelation_is_refused(Type collection, string message)
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(Target), collection]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().Be(message);
    }

    [Fact]
    public void An_abstract_declaration_that_an_override_does_not_repeat_is_refused()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(Target), typeof(AbstractOmits)]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().Be(
            "Relation 'AbstractOmits.Link' overrides 'AbstractRoot.Link', which carries [CmsRelation], without " +
            $"repeating it. [CmsRelation] is not inherited, so the relation is not served: {NotServed}. " +
            "Repeat [CmsRelation(...)], [Navigate(...)] and [SugarColumn(IsIgnore = true)] on 'AbstractOmits.Link', or remove [CmsRelation] and [Navigate] from 'AbstractRoot.Link'.");
    }

    public static TheoryData<Type> RefusedForMissingNavigate() => new()
    {
        { typeof(OverrideRelationOnly) },
        { typeof(HideRelationOnly) },
    };

    [Theory]
    [MemberData(nameof(RefusedForMissingNavigate))]
    public void A_member_that_repeats_CmsRelation_without_Navigate_is_refused_for_the_missing_Navigate(Type collection)
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(Target), collection]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().StartWith(
            $"Relation '{collection.Name}.Link' has [CmsRelation] but no [Navigate], so the relation is not served.");
    }

    public static TheoryData<Type> Accepted() => new()
    {
        { typeof(OverrideBoth) },
        { typeof(HideBoth) },
        { typeof(Inherits) },
        { typeof(ChainLeafRepeats) },
        { typeof(AbstractRepeats) },
        { typeof(ImplementsInterface) },
        { typeof(GenericRepeats) },
    };

    [Theory]
    [MemberData(nameof(Accepted))]
    public void A_declaration_the_scan_reads_is_accepted(Type collection)
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(Target), collection]);

        scan.Should().NotThrow();
    }

    [Theory]
    [InlineData(typeof(OverrideBoth))]
    [InlineData(typeof(HideBoth))]
    [InlineData(typeof(Inherits))]
    public void A_served_relation_is_in_the_scanned_relations(Type collection)
    {
        var scanned = MetadataScanner.ScanTypes([typeof(Target), collection]);

        scanned.Single(c => c.Name == MetadataScanner.CollectionNameOf(collection)).Relations
            .Should().ContainSingle(r => r.Name == "link" && r.ForeignKey == "targetId");
    }

    [Fact]
    public void An_override_that_repeats_both_relation_attributes_but_not_SugarColumn_is_refused_for_the_column()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(Target), typeof(OverrideWithoutColumn)]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().StartWith(
            "Property 'OverrideWithoutColumn.Link' overrides 'RelationBase.Link', which carries [SugarColumn], without repeating it.");
    }

    [Fact]
    public void A_leaf_that_omits_what_a_middle_class_repeated_is_refused_naming_the_middle_declaration()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(Target), typeof(LeafOmitsAfterRepeat)]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().Be(
            "Relation 'LeafOmitsAfterRepeat.Link' overrides 'MiddleRepeats.Link', which carries [CmsRelation], " +
            $"without repeating it. [CmsRelation] is not inherited, so the relation is not served: {NotServed}. " +
            "Repeat [CmsRelation(...)], [Navigate(...)] and [SugarColumn(IsIgnore = true)] on 'LeafOmitsAfterRepeat.Link', " +
            "or remove [CmsRelation] and [Navigate] from 'MiddleRepeats.Link'.");
    }

    [Fact]
    public void An_override_of_a_generic_base_declaration_is_refused()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(Target), typeof(GenericOmits)]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().StartWith(
            "Relation 'GenericOmits.Link' overrides 'GenericBase`1.Link', which carries [CmsRelation], without repeating it.");
    }

    [Fact]
    public void A_derived_declaration_that_repeats_CmsRelation_with_other_arguments_is_scanned_with_them()
    {
        var scanned = MetadataScanner.ScanTypes([typeof(Target), typeof(GenericRepeats)]);

        scanned.Single(c => c.Name == MetadataScanner.CollectionNameOf(typeof(GenericRepeats))).Relations
            .Should().ContainSingle(r => r.Name == "link" && r.DisplayTemplate == "{Name} (derived)");
    }
}
