using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

// The foreign-key name in the [Navigate] of a single-entity [CmsRelation] property (many-to-one,
// one-to-one) must be a property of the declaring entity, spelled exactly. A one-to-many child key is
// checked by the scan against the collection class (RelationDeclarationCheck); a many-to-many relation is
// not checked here.
// Why: docs/ai/decisions/relation-key-and-primary-key-declarations.md
public sealed class NavigateForeignKeyTests
{
    public sealed class Target
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    public sealed class Child
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid? ParentId { get; set; }
    }

    public sealed class Link
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid LeftId { get; set; }
        public Guid RightId { get; set; }
    }

    public sealed class ManyToOneUnknown
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid? TargetId { get; set; }

        [Navigate(NavigateType.ManyToOne, "TargetKey")]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        public Target? Target { get; set; }
    }

    public sealed class OneToOneUnknown
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid? TargetId { get; set; }

        [Navigate(NavigateType.OneToOne, "TargetKey")]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        public Target? Target { get; set; }
    }

    public sealed class ManyToOneDifferingInCase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid? TargetId { get; set; }

        [Navigate(NavigateType.ManyToOne, "targetid")]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        public Target? Target { get; set; }
    }

    public sealed class OneToOneDifferingInCase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid? TargetId { get; set; }

        [Navigate(NavigateType.OneToOne, "TARGETID")]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        public Target? Target { get; set; }
    }

    public sealed class ManyToManyUnknownKeys
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Link), "NoSuchParentKey", "NoSuchTargetKey")]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)]
        public List<Target> Targets { get; set; } = [];
    }

    public static TheoryData<Type, string, string> Unknown() => new()
    {
        { typeof(ManyToOneUnknown), "ManyToOneUnknown.Target", "ManyToOneUnknown has no property with that name" },
        { typeof(OneToOneUnknown), "OneToOneUnknown.Target", "OneToOneUnknown has no property with that name" },
    };

    [Theory]
    [MemberData(nameof(Unknown))]
    public void A_foreign_key_that_is_not_a_property_names_the_relation_the_key_and_the_type(
        Type owner, string relation, string where)
    {
        var act = () => MetadataScanner.ScanRelations(owner);

        var message = act.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain($"Relation '{relation}'").And.Contain("foreign key '").And.Contain("in [Navigate]")
            .And.Contain(where).And.Contain("Name a property of");
    }

    public static TheoryData<Type, string, string, string, string> DifferingInCase() => new()
    {
        { typeof(ManyToOneDifferingInCase), "ManyToOneDifferingInCase.Target", "targetid", "ManyToOneDifferingInCase", "TargetId" },
        { typeof(OneToOneDifferingInCase), "OneToOneDifferingInCase.Target", "TARGETID", "OneToOneDifferingInCase", "TargetId" },
    };

    [Theory]
    [MemberData(nameof(DifferingInCase))]
    public void A_key_that_differs_only_in_case_is_refused_and_names_the_property_it_misses(
        Type owner, string relation, string key, string holder, string property)
    {
        var act = () => MetadataScanner.ScanRelations(owner);

        var message = act.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain($"Relation '{relation}'").And.Contain($"foreign key '{key}'")
            .And.Contain($"{holder} has no property '{key}'; it has '{property}'")
            .And.Contain("key names are case-sensitive");
    }

    // ── one-to-many: the element type is a plain class that names no scanned collection ──

    [SugarTable("nfk_o2m_unknown")]
    [CmsCollection("O2M unknown", Name = "nfkO2mUnknown")]
    public sealed class OneToManyUnknown
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, "OwnerId")]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)]
        public List<Child> Children { get; set; } = [];
    }

    // The key exists on the declaring entity but not on the element type, which is where a one-to-many looks.
    [SugarTable("nfk_o2m_wrong_type")]
    [CmsCollection("O2M wrong type", Name = "nfkO2mWrongType")]
    public sealed class OneToManyKeyOnTheWrongType
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid? OwnerId { get; set; }

        [Navigate(NavigateType.OneToMany, "OwnerId")]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)]
        public List<Child> Children { get; set; } = [];
    }

    [SugarTable("nfk_o2m_case")]
    [CmsCollection("O2M case", Name = "nfkO2mCase")]
    public sealed class OneToManyDifferingInCase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, "PARENTID")]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)]
        public List<Child> Children { get; set; } = [];
    }

    [Theory]
    [InlineData(typeof(OneToManyUnknown), "OneToManyUnknown.Children")]
    [InlineData(typeof(OneToManyKeyOnTheWrongType), "OneToManyKeyOnTheWrongType.Children")]
    public void A_child_key_that_is_not_a_property_of_the_element_type_is_refused_at_the_scan(Type owner, string relation)
    {
        var act = () => MetadataScanner.ScanTypes([owner]);

        var message = act.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith($"Relation '{relation}' declares the foreign key 'OwnerId' in [Navigate]")
            .And.Contain("Child has no property with that name").And.Contain("Name a property of Child")
            .And.NotContain("resolves to");
    }

    [Fact]
    public void A_child_key_that_differs_only_in_case_is_refused_at_the_scan_and_names_the_property_it_misses()
    {
        var act = () => MetadataScanner.ScanTypes([typeof(OneToManyDifferingInCase)]);

        var message = act.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("Relation 'OneToManyDifferingInCase.Children'").And.Contain("foreign key 'PARENTID'")
            .And.Contain("Child has no property 'PARENTID'; it has 'ParentId'")
            .And.Contain("key names are case-sensitive");
    }

    [Fact]
    public void A_many_to_many_relation_is_not_checked()
    {
        var relations = MetadataScanner.ScanRelations(typeof(ManyToManyUnknownKeys));

        relations.Should().ContainSingle().Which.Kind.Should().Be(RelationKind.ManyToMany);
    }
}
