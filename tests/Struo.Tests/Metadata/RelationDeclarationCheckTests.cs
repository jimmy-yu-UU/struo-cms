using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// A <c>[CmsRelation]</c> declaration the item service cannot serve is refused at the scan: a missing
/// <c>[Navigate]</c>, a relation that is not a public instance property, a key property that is missing or has
/// no column, a one-to-many child key looked up by collection name, a many-to-many whose two junction keys are
/// one property, a <c>SortField</c> that is not a column of the junction or sits on a relation that is not a
/// many-to-many, and an <c>OnDelete</c> on a relation held by no many-to-one key.
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

    [Fact]
    public void A_child_key_without_a_column_on_the_element_class_that_is_the_collection_class_adds_no_clause()
    {
        var scan = () => MetadataScanner.ScanTypes(
            [typeof(OneToManyKeyHasNoColumn), typeof(Target), typeof(Kid)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("Kid.IgnoredParentId is [SugarColumn(IsIgnore = true)]")
            .And.NotContain("The key is read off");
    }

    // A virtual key that the base class ignores must have the attribute repeated on the override, which then
    // decides whether the key has a column.
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
    public void A_key_override_that_leaves_out_the_IsIgnore_of_its_base_is_refused_as_an_override()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(ParentOfAnOverridingKid), typeof(KidOverridingTheKeyWithoutTheAttribute)]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().StartWith(
            "Property 'KidOverridingTheKeyWithoutTheAttribute.ParentId' overrides 'KidBaseIgnoringTheKey.ParentId'");
    }

    [SugarTable("rdc_override_repeating_kid")]
    [CmsCollection("Override repeating kid", Name = "rdcOverrideRepeatingKid")]
    private sealed class KidRepeatingTheKeyColumn : KidBaseIgnoringTheKey
    {
        [SugarColumn(IsNullable = true)] public override Guid? ParentId { get; set; }
    }

    [SugarTable("rdc_override_repeating_parent")]
    [CmsCollection("Override repeating parent", Name = "rdcOverrideRepeatingParent")]
    private sealed class ParentOfARepeatingKid
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, "ParentId")]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)] public List<KidRepeatingTheKeyColumn> Kids { get; set; } = [];
    }

    [Fact]
    public void A_key_override_that_repeats_SugarColumn_without_IsIgnore_has_a_column_and_the_relation_is_served()
    {
        var collections = MetadataScanner.ScanTypes([typeof(ParentOfARepeatingKid), typeof(KidRepeatingTheKeyColumn)]);

        collections.Single(c => c.Name == "rdcOverrideRepeatingParent").Relations.Should().ContainSingle(r => r.Name == "kids");
    }

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

    // ── OnDelete belongs to a relation held by a many-to-one key ─────────────

    [SugarTable("rdc_od_o2m")]
    [CmsCollection("Od o2m", Name = "rdcOdO2m")]
    private sealed class OneToManyWithOnDelete
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(Kid.ParentId))]
        [CmsRelation(Interface = RelationInterface.RelatedList, OnDelete = OnDelete.SetNull)]
        [SugarColumn(IsIgnore = true)] public List<Kid> Kids { get; set; } = [];
    }

    [SugarTable("rdc_od_m2m")]
    [CmsCollection("Od m2m", Name = "rdcOdM2m")]
    private sealed class ManyToManyWithOnDelete
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, OnDelete = OnDelete.Cascade)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdc_od_m2o")]
    [CmsCollection("Od m2o", Name = "rdcOdM2o")]
    private sealed class ManyToOneWithOnDelete
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? TargetId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, OnDelete = OnDelete.SetNull)]
        [SugarColumn(IsIgnore = true)] public Target? Target { get; set; }
    }

    [SugarTable("rdc_od_o2o")]
    [CmsCollection("Od o2o", Name = "rdcOdO2o")]
    private sealed class OneToOneWithOnDelete
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? TargetId { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, OnDelete = OnDelete.Cascade)]
        [SugarColumn(IsIgnore = true)] public Target? Target { get; set; }
    }

    [SugarTable("rdc_od_two_rules")]
    [CmsCollection("Od two rules", Name = "rdcOdTwoRules")]
    private sealed class OnDeleteAndAnUnknownSortField
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, OnDelete = OnDelete.Cascade, SortField = "NoSuchSort")]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [Theory]
    [InlineData(
        typeof(OneToManyWithOnDelete), "OneToManyWithOnDelete.Kids", "SetNull", "one-to-many",
        "The many-to-one relation on Kid that points at this collection decides what happens to the children " +
        "when a parent is deleted: declare OnDelete there.")]
    [InlineData(
        typeof(ManyToManyWithOnDelete), "ManyToManyWithOnDelete.Tags", "Cascade", "many-to-many",
        "The links of a deleted item are always removed.")]
    public void OnDelete_on_a_relation_that_is_not_held_by_a_many_to_one_key_is_refused(
        Type owner, string relation, string value, string kind, string remedy)
    {
        var scan = () => MetadataScanner.ScanTypes([owner, typeof(Target), typeof(Kid)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith($"Relation '{relation}' declares OnDelete = {value} in [CmsRelation]")
            .And.Contain("what happens to the items that point at a deleted target through a many-to-one key")
            .And.Contain($"no effect on a {kind} relation").And.Contain("Remove OnDelete from it. " + remedy);
    }

    // Pins: OnDelete decides the delete of a many-to-one and a one-to-one relation.
    [Fact]
    public void OnDelete_on_a_many_to_one_or_one_to_one_relation_passes()
    {
        MetadataScanner.ScanTypes([typeof(ManyToOneWithOnDelete), typeof(Target)]).Should().HaveCount(2);
        MetadataScanner.ScanTypes([typeof(OneToOneWithOnDelete), typeof(Target)]).Should().HaveCount(2);
    }

    [Fact]
    public void OnDelete_is_reported_before_the_sort_field_of_the_same_relation()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(OnDeleteAndAnUnknownSortField), typeof(Target)]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().Contain("OnDelete = Cascade");
    }

    // ── a many-to-many names one key property per side ───────────────────────

    [SugarTable("rdc_shared_junction")]
    private sealed class SharedKeyJunction
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid LinkId { get; set; }
        public long Shared { get; set; }
    }

    [SugarTable("rdc_same_key")]
    [CmsCollection("Same key", Name = "rdcSameKey")]
    private sealed class JunctionKeysAreOneProperty
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(SharedKeyJunction), nameof(SharedKeyJunction.LinkId), nameof(SharedKeyJunction.LinkId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdc_same_key_type")]
    [CmsCollection("Same key type", Name = "rdcSameKeyType")]
    private sealed class JunctionKeysAreOnePropertyOfAnotherType
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(SharedKeyJunction), nameof(SharedKeyJunction.Shared), nameof(SharedKeyJunction.Shared))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdc_self_junction")]
    private sealed class SelfJunction
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid FromId { get; set; }
        public Guid ToId { get; set; }
    }

    [SugarTable("rdc_self_linked")]
    [CmsCollection("Self linked", Name = "rdcSelfLinked")]
    private sealed class SelfLinked
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(SelfJunction), nameof(SelfJunction.FromId), nameof(SelfJunction.ToId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<SelfLinked> Related { get; set; } = [];
    }

    [Fact]
    public void A_many_to_many_that_names_one_property_for_both_keys_is_refused()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(JunctionKeysAreOneProperty), typeof(Target)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith("Relation 'JunctionKeysAreOneProperty.Tags' declares the same junction property " +
                                   "'LinkId' as parent key and as target key in [Navigate]")
            .And.Contain("a link would store both ids in one column")
            .And.Contain("SharedKeyJunction needs one key property per side");
    }

    [Fact]
    public void Keys_that_are_one_property_are_reported_before_the_key_types_are_compared()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(JunctionKeysAreOnePropertyOfAnotherType), typeof(Target)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("the same junction property 'Shared'").And.NotContain("primary key");
    }

    // Pin: a self-referencing many-to-many names two different properties of the junction.
    [Fact]
    public void A_self_referencing_many_to_many_with_two_key_properties_passes() =>
        MetadataScanner.ScanTypes([typeof(SelfLinked)]).Should().ContainSingle();

    // ── a relation is a public instance property ─────────────────────────────

    [SugarTable("rdc_vis_private")]
    [CmsCollection("Vis private", Name = "rdcVisPrivate")]
    private sealed class PrivateRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] private List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdc_vis_internal")]
    [CmsCollection("Vis internal", Name = "rdcVisInternal")]
    private sealed class InternalRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] internal List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdc_vis_protected")]
    [CmsCollection("Vis protected", Name = "rdcVisProtected")]
    private class ProtectedRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] protected List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdc_vis_private_protected")]
    [CmsCollection("Vis private protected", Name = "rdcVisPrivateProtected")]
    private class PrivateProtectedRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] private protected List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdc_vis_protected_internal")]
    [CmsCollection("Vis protected internal", Name = "rdcVisProtectedInternal")]
    private class ProtectedInternalRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] protected internal List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdc_vis_static")]
    [CmsCollection("Vis static", Name = "rdcVisStatic")]
    private sealed class StaticRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public static List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdc_vis_private_static")]
    [CmsCollection("Vis private static", Name = "rdcVisPrivateStatic")]
    private sealed class PrivateStaticRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] private static List<Target> Tags { get; set; } = [];
    }

    private abstract class BaseWithPrivateRelation
    {
        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] private List<Target> Hidden { get; set; } = [];
    }

    [SugarTable("rdc_vis_base_private")]
    [CmsCollection("Vis base private", Name = "rdcVisBasePrivate")]
    private sealed class SubclassOfAPrivateRelation : BaseWithPrivateRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    private abstract class BaseWithPublicRelation
    {
        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdc_vis_base_public")]
    [CmsCollection("Vis base public", Name = "rdcVisBasePublic")]
    private sealed class SubclassOfAPublicRelation : BaseWithPublicRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [SugarTable("rdc_vis_two_rules")]
    [CmsCollection("Vis two rules", Name = "rdcVisTwoRules")]
    private sealed class PrivateRelationAndAMissingNavigate
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Unnavigated { get; set; } = [];

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] private List<Target> Hidden { get; set; } = [];
    }

    public static TheoryData<Type, string, string> NonPublicInstanceRelations() => new()
    {
        { typeof(PrivateRelation), "PrivateRelation.Tags", "not public" },
        { typeof(InternalRelation), "InternalRelation.Tags", "not public" },
        { typeof(ProtectedRelation), "ProtectedRelation.Tags", "not public" },
        { typeof(PrivateProtectedRelation), "PrivateProtectedRelation.Tags", "not public" },
        { typeof(ProtectedInternalRelation), "ProtectedInternalRelation.Tags", "not public" },
        { typeof(StaticRelation), "StaticRelation.Tags", "static" },
        { typeof(PrivateStaticRelation), "PrivateStaticRelation.Tags", "static" },
        { typeof(SubclassOfAPrivateRelation), "BaseWithPrivateRelation.Hidden", "not public" },
    };

    [Theory]
    [MemberData(nameof(NonPublicInstanceRelations))]
    public void A_relation_that_is_not_a_public_instance_property_is_refused_and_names_what_it_is(
        Type owner, string relation, string what)
    {
        var scan = () => MetadataScanner.ScanTypes([owner, typeof(Target)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith($"Relation '{relation}' has [CmsRelation] but is {what}")
            .And.Contain("the relation is not served")
            .And.Contain("A relation is a public instance property");
    }

    // Pin: a public instance relation declared on a base class is read.
    [Fact]
    public void A_public_instance_relation_declared_on_a_base_class_passes() =>
        MetadataScanner.ScanTypes([typeof(SubclassOfAPublicRelation), typeof(Target)]).Should().HaveCount(2);

    [Fact]
    public void A_non_public_relation_is_reported_before_a_missing_navigate_of_the_same_class()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(PrivateRelationAndAMissingNavigate), typeof(Target)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().StartWith("Relation 'PrivateRelationAndAMissingNavigate.Hidden' has [CmsRelation]")
            .And.Contain("but is not public");
    }
}
