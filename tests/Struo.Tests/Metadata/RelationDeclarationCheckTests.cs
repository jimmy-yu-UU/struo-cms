using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// A <c>[CmsRelation]</c> declaration the item service cannot serve is refused at the scan: a
/// <c>[CmsRelation]</c> without <c>[Navigate]</c>, a <c>SortField</c> that is not a column of the junction
/// or sits on a relation that is not a many-to-many, a key property that has no column, and a one-to-many
/// child key the collection class does not declare.
/// </summary>
public class RelationDeclarationCheckTests
{
    [SugarTable("rdc_target")]
    [CmsCollection("Target", Name = "rdcTarget")]
    private sealed class Target
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [SugarTable("rdc_junction")]
    private sealed class Junction
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid TargetId { get; set; }
        public int Sort { get; set; }
        public string SortText { get; set; } = "";
        [SugarColumn(IsIgnore = true)] public int SortIgnored { get; set; }
        [SugarColumn(IsIgnore = true)] public Guid IgnoredOwnerId { get; set; }
        [SugarColumn(IsIgnore = true)] public Guid IgnoredTargetId { get; set; }
    }

    [SugarTable("rdc_kid")]
    [CmsCollection("Kid", Name = "rdcKid")]
    private sealed class Kid
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? ParentId { get; set; }
        [SugarColumn(IsIgnore = true)] public Guid? IgnoredParentId { get; set; }
        public int Sort { get; set; }
    }

    // ── [CmsRelation] without [Navigate] ─────────────────────────────────────

    [SugarTable("rdc_no_nav_single")]
    [CmsCollection("No nav single", Name = "rdcNoNavSingle")]
    private sealed class SingleWithoutNavigate
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? TargetId { get; set; }

        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)] public Target? Target { get; set; }
    }

    [SugarTable("rdc_no_nav_list")]
    [CmsCollection("No nav list", Name = "rdcNoNavList")]
    private sealed class ListWithoutNavigate
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    private abstract class BaseWithoutNavigate
    {
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdc_no_nav_sub")]
    [CmsCollection("No nav sub", Name = "rdcNoNavSub")]
    private sealed class SubclassWithoutNavigate : BaseWithoutNavigate
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [SugarTable("rdc_nav_only")]
    [CmsCollection("Nav only", Name = "rdcNavOnly")]
    private sealed class NavigateWithoutCmsRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? TargetId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(TargetId))]
        [SugarColumn(IsIgnore = true)] public Target? Target { get; set; }
    }

    [Theory]
    [InlineData(typeof(SingleWithoutNavigate), "SingleWithoutNavigate.Target")]
    [InlineData(typeof(ListWithoutNavigate), "ListWithoutNavigate.Tags")]
    public void A_cms_relation_without_navigate_is_refused_and_names_the_two_supported_forms(
        Type owner, string relation)
    {
        var scan = () => MetadataScanner.ScanTypes([owner, typeof(Target)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith($"Relation '{relation}' has [CmsRelation] but no [Navigate]")
            .And.Contain("the relation is not served").And.Contain("Supported: [Navigate(NavigateType, foreignKey)]")
            .And.Contain("[Navigate(junctionType, parentKey, targetKey)] for many-to-many");
    }

    [Fact]
    public void The_navigate_message_names_the_class_that_declares_the_relation()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(SubclassWithoutNavigate), typeof(Target)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().StartWith("Relation 'BaseWithoutNavigate.Tags'").And.NotContain("SubclassWithoutNavigate");
    }

    [Fact]
    public void A_navigate_without_cms_relation_is_not_a_relation() =>
        MetadataScanner.ScanTypes([typeof(NavigateWithoutCmsRelation), typeof(Target)]).Should().HaveCount(2);

    // ── SortField of a many-to-many ──────────────────────────────────────────

    [SugarTable("rdc_sort_ok")]
    [CmsCollection("Sort ok", Name = "rdcSortOk")]
    private sealed class SortFieldOk
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(Junction.Sort))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdc_sort_string")]
    [CmsCollection("Sort string", Name = "rdcSortString")]
    private sealed class SortFieldIsAString
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(Junction.SortText))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdc_sort_unknown")]
    [CmsCollection("Sort unknown", Name = "rdcSortUnknown")]
    private sealed class SortFieldNamesNoProperty
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = "NoSuchSort")]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdc_sort_case")]
    [CmsCollection("Sort case", Name = "rdcSortCase")]
    private sealed class SortFieldInOtherCase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = "sort")]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdc_sort_ignored")]
    [CmsCollection("Sort ignored", Name = "rdcSortIgnored")]
    private sealed class SortFieldHasNoColumn
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = nameof(Junction.SortIgnored))]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [Fact]
    public void A_sort_field_that_is_not_a_property_of_the_junction_names_the_relation_the_field_and_the_junction()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(SortFieldNamesNoProperty), typeof(Target)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith(
                "Relation 'SortFieldNamesNoProperty.Tags' declares SortField 'NoSuchSort' in [CmsRelation]")
            .And.Contain("Junction has no property with that name")
            .And.Contain("Name a property of Junction that holds the sort field, or add 'NoSuchSort' to Junction");
    }

    [Fact]
    public void A_sort_field_that_differs_only_in_case_is_refused_and_names_the_property_it_misses()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(SortFieldInOtherCase), typeof(Target)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("SortFieldInOtherCase.Tags").And.Contain("SortField 'sort'")
            .And.Contain("Junction has no property 'sort'; it has 'Sort'")
            .And.Contain("sort field names are case-sensitive").And.Contain("Write the sort field as 'Sort'");
    }

    [Fact]
    public void A_sort_field_without_a_column_is_refused_and_says_the_links_cannot_be_ordered()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(SortFieldHasNoColumn), typeof(Target)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith("Relation 'SortFieldHasNoColumn.Tags' declares SortField 'SortIgnored'")
            .And.Contain("Junction.SortIgnored is [SugarColumn(IsIgnore = true)]")
            .And.Contain("the links cannot be ordered by it").And.Contain("stored in a column");
    }

    // Pins: a numeric and a string sort property both order the links when read back.
    [Fact]
    public void A_numeric_or_string_sort_field_of_the_junction_passes()
    {
        MetadataScanner.ScanTypes([typeof(SortFieldOk), typeof(Target)]).Should().HaveCount(2);
        MetadataScanner.ScanTypes([typeof(SortFieldIsAString), typeof(Target)]).Should().HaveCount(2);
    }

    // ── SortField on a relation that is not a many-to-many ───────────────────

    [SugarTable("rdc_sort_m2o")]
    [CmsCollection("Sort m2o", Name = "rdcSortM2o")]
    private sealed class SortFieldOnManyToOne
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? TargetId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, SortField = "Sort")]
        [SugarColumn(IsIgnore = true)] public Target? Target { get; set; }
    }

    [SugarTable("rdc_sort_o2m")]
    [CmsCollection("Sort o2m", Name = "rdcSortO2m")]
    private sealed class SortFieldOnOneToMany
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(Kid.ParentId))]
        [CmsRelation(Interface = RelationInterface.RelatedList, SortField = nameof(Kid.Sort))]
        [SugarColumn(IsIgnore = true)] public List<Kid> Kids { get; set; } = [];
    }

    [SugarTable("rdc_sort_o2o")]
    [CmsCollection("Sort o2o", Name = "rdcSortO2o")]
    private sealed class SortFieldOnOneToOne
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? TargetId { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, SortField = "Sort")]
        [SugarColumn(IsIgnore = true)] public Target? Target { get; set; }
    }

    [Theory]
    [InlineData(typeof(SortFieldOnManyToOne), "SortFieldOnManyToOne.Target", "Sort")]
    [InlineData(typeof(SortFieldOnOneToOne), "SortFieldOnOneToOne.Target", "Sort")]
    [InlineData(typeof(SortFieldOnOneToMany), "SortFieldOnOneToMany.Kids", "Sort")]
    public void A_sort_field_on_a_relation_that_is_not_a_many_to_many_is_refused(
        Type owner, string relation, string field)
    {
        var scan = () => MetadataScanner.ScanTypes([owner, typeof(Target), typeof(Kid)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith($"Relation '{relation}' declares SortField '{field}'")
            .And.Contain("SortField orders the links of a many-to-many relation")
            .And.Contain("this relation is not one").And.Contain("Remove SortField from it");
    }

    // ── a key property needs a column ────────────────────────────────────────

    [SugarTable("rdc_key_m2o")]
    [CmsCollection("Key m2o", Name = "rdcKeyM2o")]
    private sealed class ManyToOneKeyHasNoColumn
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsIgnore = true)] public Guid? TargetId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)] public Target? Target { get; set; }
    }

    [SugarTable("rdc_key_o2m")]
    [CmsCollection("Key o2m", Name = "rdcKeyO2m")]
    private sealed class OneToManyKeyHasNoColumn
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(Kid.IgnoredParentId))]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)] public List<Kid> Kids { get; set; } = [];
    }

    [SugarTable("rdc_key_jp")]
    [CmsCollection("Key jp", Name = "rdcKeyJp")]
    private sealed class JunctionParentKeyHasNoColumn
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.IgnoredOwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdc_key_jt")]
    [CmsCollection("Key jt", Name = "rdcKeyJt")]
    private sealed class JunctionTargetKeyHasNoColumn
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.IgnoredTargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    public static TheoryData<Type, string, string, string> KeysWithoutColumn() => new()
    {
        {
            typeof(ManyToOneKeyHasNoColumn), "ManyToOneKeyHasNoColumn.Target",
            "foreign key 'TargetId'", "ManyToOneKeyHasNoColumn.TargetId"
        },
        { typeof(OneToManyKeyHasNoColumn), "OneToManyKeyHasNoColumn.Kids", "foreign key 'IgnoredParentId'", "Kid.IgnoredParentId" },
        {
            typeof(JunctionParentKeyHasNoColumn), "JunctionParentKeyHasNoColumn.Tags",
            "junction parent key 'IgnoredOwnerId'", "Junction.IgnoredOwnerId"
        },
        {
            typeof(JunctionTargetKeyHasNoColumn), "JunctionTargetKeyHasNoColumn.Tags",
            "junction target key 'IgnoredTargetId'", "Junction.IgnoredTargetId"
        },
    };

    [Theory]
    [MemberData(nameof(KeysWithoutColumn))]
    public void A_key_property_without_a_column_names_the_relation_the_key_and_the_class(
        Type owner, string relation, string declaration, string property)
    {
        var scan = () => MetadataScanner.ScanTypes([owner, typeof(Target), typeof(Kid)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith($"Relation '{relation}' declares the {declaration} in [Navigate]")
            .And.Contain($"{property} is [SugarColumn(IsIgnore = true)]")
            .And.Contain("the relation cannot read or store the key").And.Contain("stored in a column");
    }

    // ── a one-to-many child key is read off the collection class ─────────────

    private static class OtherScope
    {
        public sealed class Kid
        {
            public Guid Id { get; set; }
            public Guid? ParentId { get; set; }
        }
    }

    [SugarTable("rdc_plain_parent")]
    [CmsCollection("Plain parent", Name = "rdcPlainParent")]
    private sealed class OneToManyOverAPlainClassNamedLikeAKid
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, "ParentId")]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)] public List<OtherScope.Kid> Kids { get; set; } = [];
    }

    // A collection named like OtherScope.Kid whose class does not declare the child key.
    [SugarTable("rdc_keyless_kid")]
    [CmsCollection("Kid", Name = "kid")]
    private sealed class KidWithoutTheChildKey
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [Fact]
    public void A_child_key_the_collection_class_does_not_declare_is_refused_although_the_plain_element_class_does()
    {
        var scan = () => MetadataScanner.ScanTypes(
            [typeof(OneToManyOverAPlainClassNamedLikeAKid), typeof(KidWithoutTheChildKey)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith(
                "Relation 'OneToManyOverAPlainClassNamedLikeAKid.Kids' declares the foreign key 'ParentId' in [Navigate]")
            .And.Contain("but KidWithoutTheChildKey (the collection the element type OtherScope.Kid resolves to by name 'kid') " +
                         "has no property with that name")
            .And.Contain("Name a property of KidWithoutTheChildKey").And.Contain("add 'ParentId' to KidWithoutTheChildKey");
    }

    [Fact]
    public void A_plain_element_class_that_names_no_collection_keeps_its_own_child_key() =>
        MetadataScanner.ScanTypes([typeof(OneToManyOverAPlainClassNamedLikeAKid)]).Should().ContainSingle();

    private static class ScopeWithoutTheKey
    {
        public sealed class ResolvedKid
        {
            public Guid Id { get; set; }
        }
    }

    // The collection ScopeWithoutTheKey.ResolvedKid is named after declares the child key.
    [SugarTable("rdc_resolved_kid")]
    [CmsCollection("Resolved kid", Name = "resolvedKid")]
    private sealed class KidResolvedByName
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? ParentId { get; set; }
    }

    [SugarTable("rdc_plain_no_key_parent")]
    [CmsCollection("Plain no key parent", Name = "rdcPlainNoKeyParent")]
    private sealed class OneToManyOverAPlainClassWithoutTheKey
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, "ParentId")]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)] public List<ScopeWithoutTheKey.ResolvedKid> Kids { get; set; } = [];
    }

    [Fact]
    public void A_child_key_the_collection_class_declares_passes_although_the_plain_element_class_does_not() =>
        MetadataScanner.ScanTypes([typeof(OneToManyOverAPlainClassWithoutTheKey), typeof(KidResolvedByName)])
            .Should().HaveCount(2);

    [Fact]
    public void A_plain_element_class_without_the_key_is_refused_when_it_names_no_collection()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(OneToManyOverAPlainClassWithoutTheKey)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("but ResolvedKid has no property with that name").And.NotContain("resolves to");
    }

    [SugarTable("rdc_unknown_key_parent")]
    [CmsCollection("Unknown key parent", Name = "rdcUnknownKeyParent")]
    private sealed class OneToManyNamingAKeyKidDoesNotDeclare
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, "NoSuchKey")]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)] public List<Kid> Kids { get; set; } = [];
    }

    [Fact]
    public void A_child_key_named_on_the_element_class_that_is_the_collection_class_adds_no_resolution_clause()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(OneToManyNamingAKeyKidDoesNotDeclare), typeof(Kid)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("but Kid has no property with that name").And.NotContain("resolves to");
    }

    // A virtual key that the base class ignores and the override does not repeat has a column (SqlSugar reads
    // [SugarColumn] off the overriding property), so the relation is served; an override that ignores it has none.
    private abstract class KidBaseIgnoringTheKey
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsIgnore = true)] public virtual Guid? ParentId { get; set; }
    }

    [SugarTable("rdc_override_kid")]
    [CmsCollection("Override kid", Name = "rdcOverrideKid")]
    private sealed class KidOverridingTheKeyWithoutTheAttribute : KidBaseIgnoringTheKey
    {
        public override Guid? ParentId { get; set; }
    }

    private abstract class KidBaseDeclaringTheKey
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public virtual Guid? ParentId { get; set; }
    }

    [SugarTable("rdc_override_ignored_kid")]
    [CmsCollection("Override ignored kid", Name = "rdcOverrideIgnoredKid")]
    private sealed class KidOverridingTheKeyAndIgnoringIt : KidBaseDeclaringTheKey
    {
        [SugarColumn(IsIgnore = true)] public override Guid? ParentId { get; set; }
    }

    [SugarTable("rdc_override_parent")]
    [CmsCollection("Override parent", Name = "rdcOverrideParent")]
    private sealed class ParentOfAnOverridingKid
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, "ParentId")]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)] public List<KidOverridingTheKeyWithoutTheAttribute> Kids { get; set; } = [];
    }

    [SugarTable("rdc_override_ignored_parent")]
    [CmsCollection("Override ignored parent", Name = "rdcOverrideIgnoredParent")]
    private sealed class ParentOfAnIgnoringKid
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, "ParentId")]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)] public List<KidOverridingTheKeyAndIgnoringIt> Kids { get; set; } = [];
    }

    [Fact]
    public void A_key_the_override_does_not_repeat_IsIgnore_on_has_a_column() =>
        MetadataScanner.ScanTypes([typeof(ParentOfAnOverridingKid), typeof(KidOverridingTheKeyWithoutTheAttribute)])
            .Should().HaveCount(2);

    [Fact]
    public void A_key_the_override_marks_IsIgnore_has_no_column()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(ParentOfAnIgnoringKid), typeof(KidOverridingTheKeyAndIgnoringIt)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("KidOverridingTheKeyAndIgnoringIt.ParentId is [SugarColumn(IsIgnore = true)]");
    }

    // ── order when a declaration breaks two rules ────────────────────────────

    [SugarTable("rdc_two_rules")]
    [CmsCollection("Two rules", Name = "rdcTwoRules")]
    private sealed class KeyWithoutColumnAndOfAnotherType
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsIgnore = true)] public long? TargetId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)] public Target? Target { get; set; }
    }

    [Fact]
    public void A_key_without_a_column_is_reported_before_the_key_type_is_compared()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(KeyWithoutColumnAndOfAnotherType), typeof(Target)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("is [SugarColumn(IsIgnore = true)]").And.NotContain("primary key");
    }

    [SugarTable("rdc_nav_and_sort")]
    [CmsCollection("Nav and sort", Name = "rdcNavAndSort")]
    private sealed class MissingNavigateAndAnUnknownSortField
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = "NoSuchSort")]
        [SugarColumn(IsIgnore = true)] public List<Target> Sorted { get; set; } = [];

        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Unnavigated { get; set; } = [];
    }

    [Fact]
    public void A_missing_navigate_is_reported_before_the_other_relation_checks_of_the_same_type()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(MissingNavigateAndAnUnknownSortField), typeof(Target)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().StartWith("Relation 'MissingNavigateAndAnUnknownSortField.Unnavigated'");
    }
}
