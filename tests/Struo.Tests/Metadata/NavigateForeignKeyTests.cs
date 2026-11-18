using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

// The foreign-key name in a [CmsRelation] property's [Navigate] must be a property: of the declaring
// entity for many-to-one and one-to-one, of the list's element type for one-to-many. Names match without
// regard to case. A many-to-many relation names its keys on the junction type and is not checked here.
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

    public sealed class OneToManyUnknown
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, "OwnerId")]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)]
        public List<Child> Children { get; set; } = [];
    }

    // The key exists on the declaring entity but not on the element type, which is where a one-to-many looks.
    public sealed class OneToManyKeyOnTheWrongType
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid? OwnerId { get; set; }

        [Navigate(NavigateType.OneToMany, "OwnerId")]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)]
        public List<Child> Children { get; set; } = [];
    }

    public sealed class DifferingOnlyInCase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid? TargetId { get; set; }

        [Navigate(NavigateType.ManyToOne, "targetid")]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        public Target? Target { get; set; }

        [Navigate(NavigateType.OneToMany, "PARENTID")]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)]
        public List<Child> Children { get; set; } = [];
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
        { typeof(OneToManyUnknown), "OneToManyUnknown.Children", "Child has no property with that name" },
        { typeof(OneToManyKeyOnTheWrongType), "OneToManyKeyOnTheWrongType.Children", "Child has no property with that name" },
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

    [Fact]
    public void A_name_that_differs_only_in_case_is_accepted()
    {
        var relations = MetadataScanner.ScanRelations(typeof(DifferingOnlyInCase));

        relations.Select(r => r.Name).Should().BeEquivalentTo("target", "children");
    }

    [Fact]
    public void A_many_to_many_relation_is_not_checked()
    {
        var relations = MetadataScanner.ScanRelations(typeof(ManyToManyUnknownKeys));

        relations.Should().ContainSingle().Which.Kind.Should().Be(RelationKind.ManyToMany);
    }
}
