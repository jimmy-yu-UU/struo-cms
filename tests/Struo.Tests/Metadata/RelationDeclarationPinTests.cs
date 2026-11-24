using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// Legal relation forms the declaration checks leave alone (marked as pins), and the one static-on-a-base-class
/// form they refuse.
/// </summary>
public class RelationDeclarationPinTests
{
    [SugarTable("rdp_target")]
    [CmsCollection("Target", Name = "rdpTarget")]
    private sealed class Target
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [SugarTable("rdp_junction")]
    private sealed class Junction
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid TargetId { get; set; }
    }

    [SugarTable("rdp_kid")]
    [CmsCollection("Kid", Name = "rdpKid")]
    private sealed class Kid
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? ParentId { get; set; }
    }

    // ── a relation property with a private setter ────────────────────────────

    [SugarTable("rdp_private_set")]
    [CmsCollection("Private set", Name = "rdpPrivateSet")]
    private sealed class RelationWithPrivateSet
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; private set; } = [];
    }

    // Pin: the scan reads the getter of a relation property; the setter's visibility is not part of the rule.
    [Fact]
    public void A_relation_with_a_public_getter_and_a_private_setter_passes_and_is_in_the_metadata()
    {
        var collections = MetadataScanner.ScanTypes([typeof(RelationWithPrivateSet), typeof(Target)]);

        collections.Single(c => c.Name == "rdpPrivateSet").Relations.Should().ContainSingle(r => r.Name == "tags");
    }

    // ── a child key inherited from a base class ──────────────────────────────

    private abstract class KidBaseWithTheKey
    {
        [SugarColumn(IsNullable = true)] public Guid? ParentId { get; set; }
    }

    private abstract class KidBaseIgnoringTheKey
    {
        [SugarColumn(IsIgnore = true)] public Guid? ParentId { get; set; }
    }

    [SugarTable("rdp_inheriting_kid")]
    [CmsCollection("Inheriting kid", Name = "rdpInheritingKid")]
    private sealed class KidInheritingTheKey : KidBaseWithTheKey
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [SugarTable("rdp_ignoring_kid")]
    [CmsCollection("Ignoring kid", Name = "rdpIgnoringKid")]
    private sealed class KidInheritingAnIgnoredKey : KidBaseIgnoringTheKey
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [SugarTable("rdp_parent_inherited")]
    [CmsCollection("Parent inherited", Name = "rdpParentInherited")]
    private sealed class ParentOfAKidInheritingTheKey
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(KidBaseWithTheKey.ParentId))]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)] public List<KidInheritingTheKey> Kids { get; set; } = [];
    }

    [SugarTable("rdp_parent_ignored")]
    [CmsCollection("Parent ignored", Name = "rdpParentIgnored")]
    private sealed class ParentOfAKidInheritingAnIgnoredKey
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(KidBaseIgnoringTheKey.ParentId))]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)] public List<KidInheritingAnIgnoredKey> Kids { get; set; } = [];
    }

    // Pin: a key declared on a base class and not overridden is the key of the derived class.
    [Fact]
    public void A_child_key_inherited_from_a_base_class_that_has_a_column_passes() =>
        MetadataScanner.ScanTypes([typeof(ParentOfAKidInheritingTheKey), typeof(KidInheritingTheKey)])
            .Should().HaveCount(2);

    [Fact]
    public void A_child_key_inherited_from_a_base_class_that_ignores_it_is_refused_naming_the_base_class()
    {
        var scan = () => MetadataScanner.ScanTypes(
            [typeof(ParentOfAKidInheritingAnIgnoredKey), typeof(KidInheritingAnIgnoredKey)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().StartWith("Relation 'ParentOfAKidInheritingAnIgnoredKey.Kids' declares the foreign key")
            .And.Contain("KidBaseIgnoringTheKey.ParentId is [SugarColumn(IsIgnore = true)]")
            .And.NotContain("The key is read off");
    }

    // ── an explicit OnDelete = Restrict ──────────────────────────────────────

    [SugarTable("rdp_restrict_o2m")]
    [CmsCollection("Restrict o2m", Name = "rdpRestrictO2m")]
    private sealed class OneToManyWithRestrict
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(Kid.ParentId))]
        [CmsRelation(Interface = RelationInterface.RelatedList, OnDelete = OnDelete.Restrict)]
        [SugarColumn(IsIgnore = true)] public List<Kid> Kids { get; set; } = [];
    }

    [SugarTable("rdp_restrict_m2m")]
    [CmsCollection("Restrict m2m", Name = "rdpRestrictM2m")]
    private sealed class ManyToManyWithRestrict
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect, OnDelete = OnDelete.Restrict)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    // Pin: Restrict is the default, so writing it out is no declaration of a rule.
    [Fact]
    public void An_explicit_restrict_on_a_one_to_many_or_many_to_many_relation_passes()
    {
        MetadataScanner.ScanTypes([typeof(OneToManyWithRestrict), typeof(Kid)]).Should().HaveCount(2);
        MetadataScanner.ScanTypes([typeof(ManyToManyWithRestrict), typeof(Target)]).Should().HaveCount(2);
    }

    // ── a static relation on a base class ────────────────────────────────────

    private abstract class BaseWithStaticRelation
    {
        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public static List<Target> Tags { get; set; } = [];
    }

    [SugarTable("rdp_static_sub")]
    [CmsCollection("Static sub", Name = "rdpStaticSub")]
    private sealed class SubclassOfAStaticRelation : BaseWithStaticRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [Fact]
    public void A_static_relation_declared_on_a_base_class_is_refused_and_names_the_base_class()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(SubclassOfAStaticRelation), typeof(Target)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().StartWith("Relation 'BaseWithStaticRelation.Tags' has [CmsRelation] but is static")
            .And.Contain("declare it public and without static");
    }
}
