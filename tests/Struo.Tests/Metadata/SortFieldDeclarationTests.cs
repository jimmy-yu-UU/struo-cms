using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// A many-to-many <c>SortField</c> is a junction property the link writer can reach: a blank name is refused
/// with its own message, and a setter that is private on a base class of the junction is refused, while a
/// protected or init setter on the base class passes.
/// </summary>
public class SortFieldDeclarationTests
{
    [SugarTable("sfd_target")]
    [CmsCollection("Sfd target", Name = "sfdTarget")]
    private sealed class Target
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [SugarTable("sfd_plain_junction")]
    private sealed class PlainJunction
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid TargetId { get; set; }
        public int Sort { get; set; }
    }

    private abstract class SortWithPrivateSet { public int Sort { get; private set; } }

    private abstract class SortWithProtectedSet { public int Sort { get; protected set; } }

    private abstract class SortWithInitSet { public int Sort { get; init; } }

    private abstract class SortWithoutSetter { public int Sort { get; } }

    [SugarTable("sfd_private_junction")]
    private sealed class PrivateOnBaseJunction : SortWithPrivateSet
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid TargetId { get; set; }
    }

    [SugarTable("sfd_protected_junction")]
    private sealed class ProtectedOnBaseJunction : SortWithProtectedSet
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid TargetId { get; set; }
    }

    [SugarTable("sfd_init_junction")]
    private sealed class InitOnBaseJunction : SortWithInitSet
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid TargetId { get; set; }
    }

    [SugarTable("sfd_nosetter_junction")]
    private sealed class NoSetterOnBaseJunction : SortWithoutSetter
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid TargetId { get; set; }
    }

    [SugarTable("sfd_blank_empty")]
    [CmsCollection("Sfd blank empty", Name = "sfdBlankEmpty")]
    private sealed class EmptySortField
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(PlainJunction), nameof(PlainJunction.OwnerId), nameof(PlainJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = "")]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfd_blank_space")]
    [CmsCollection("Sfd blank space", Name = "sfdBlankSpace")]
    private sealed class WhitespaceSortField
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(PlainJunction), nameof(PlainJunction.OwnerId), nameof(PlainJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = "  ")]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfd_blank_m2o")]
    [CmsCollection("Sfd blank m2o", Name = "sfdBlankM2o")]
    private sealed class BlankSortFieldOnManyToOne
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? TargetId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, SortField = "")]
        [SugarColumn(IsIgnore = true)] public Target? Target { get; set; }
    }

    [SugarTable("sfd_o_private")]
    [CmsCollection("Sfd private", Name = "sfdPrivate")]
    private sealed class OrderedByPrivateOnBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(PrivateOnBaseJunction), nameof(PrivateOnBaseJunction.OwnerId),
            nameof(PrivateOnBaseJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = "Sort")]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfd_o_protected")]
    [CmsCollection("Sfd protected", Name = "sfdProtected")]
    private sealed class OrderedByProtectedOnBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(ProtectedOnBaseJunction), nameof(ProtectedOnBaseJunction.OwnerId),
            nameof(ProtectedOnBaseJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = "Sort")]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfd_o_init")]
    [CmsCollection("Sfd init", Name = "sfdInit")]
    private sealed class OrderedByInitOnBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(InitOnBaseJunction), nameof(InitOnBaseJunction.OwnerId),
            nameof(InitOnBaseJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = "Sort")]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("sfd_o_nosetter")]
    [CmsCollection("Sfd nosetter", Name = "sfdNoSetter")]
    private sealed class OrderedByNoSetterOnBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(NoSetterOnBaseJunction), nameof(NoSetterOnBaseJunction.OwnerId),
            nameof(NoSetterOnBaseJunction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, SortField = "Sort")]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [Theory]
    [InlineData(typeof(EmptySortField), "EmptySortField.Tags")]
    [InlineData(typeof(WhitespaceSortField), "WhitespaceSortField.Tags")]
    public void A_blank_sort_field_is_refused_and_says_to_name_a_junction_property_or_remove_it(
        Type owner, string relation)
    {
        var scan = () => MetadataScanner.ScanTypes([owner, typeof(Target)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith($"Relation '{relation}' declares a blank SortField in [CmsRelation]")
            .And.Contain("which names no junction property, so the links keep no order")
            .And.Contain("Name a property of PlainJunction that holds the position")
            .And.Contain("or remove SortField from it")
            .And.NotContain("has no property with that name");
    }

    [Fact]
    public void A_blank_sort_field_on_a_relation_that_is_not_a_many_to_many_is_refused_as_a_sort_field_there()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(BlankSortFieldOnManyToOne), typeof(Target)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("SortField orders the links of a many-to-many relation and this relation is not one")
            .And.NotContain("blank");
    }

    [Fact]
    public void A_sort_property_with_a_private_setter_on_a_base_class_is_refused_and_says_it_cannot_be_reached()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(OrderedByPrivateOnBase), typeof(Target)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith("Relation 'OrderedByPrivateOnBase.Tags' declares SortField 'Sort' in [CmsRelation]")
            .And.Contain("SortWithPrivateSet.Sort has a private setter on the base class SortWithPrivateSet")
            .And.Contain("which PrivateOnBaseJunction cannot reach")
            .And.Contain("Make the setter protected, init or public on SortWithPrivateSet")
            .And.NotContain("has no setter");
    }

    [Fact]
    public void A_sort_property_without_any_setter_on_a_base_class_is_refused_as_having_no_setter()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(OrderedByNoSetterOnBase), typeof(Target)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("SortWithoutSetter.Sort has no setter").And.NotContain("private setter");
    }

    // Pins: a protected or init setter on a base class is reachable from the junction class.
    [Theory]
    [InlineData(typeof(OrderedByProtectedOnBase))]
    [InlineData(typeof(OrderedByInitOnBase))]
    public void A_sort_property_with_a_protected_or_init_setter_on_a_base_class_passes(Type owner) =>
        MetadataScanner.ScanTypes([owner, typeof(Target)]).Should().HaveCount(2);
}
