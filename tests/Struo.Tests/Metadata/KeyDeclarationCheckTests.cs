using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// The keys a collection declares must exist: both junction keys of a many-to-many relation name a
/// property of the junction type, and every collection has exactly one primary key property. The scan
/// refuses anything else with a message that names the class to change.
/// </summary>
public class KeyDeclarationCheckTests
{
    [SugarTable("kdc_target")]
    [CmsCollection("Target", Name = "kdcTarget")]
    private sealed class Target : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    }

    // ── many-to-many junction keys ───────────────────────────────────────────

    [SugarTable("kdc_junction")]
    private sealed class Junction
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid OwnerId { get; set; }
        public Guid TargetId { get; set; }
    }

    private abstract class JunctionKeysBase
    {
        public Guid OwnerId { get; set; }
        public Guid TargetId { get; set; }
    }

    [SugarTable("kdc_junction_inherited")]
    private sealed class JunctionWithInheritedKeys : JunctionKeysBase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [SugarTable("kdc_junction_collection")]
    [CmsCollection("Junction collection", Name = "kdcJunctionCollection")]
    private sealed class JunctionCollection
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Owner", Interface = FieldInterface.Uuid, Sort = 1)] public Guid OwnerId { get; set; }
        [CmsField(Label = "Target", Interface = FieldInterface.Uuid, Sort = 2)] public Guid TargetId { get; set; }
    }

    [SugarTable("kdc_junction_two_keys")]
    [CmsCollection("Junction two keys", Name = "kdcJunctionTwoKeys")]
    private sealed class JunctionCollectionWithTwoPrimaryKeys
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsPrimaryKey = true)]
        [CmsField(Label = "Owner", Interface = FieldInterface.Uuid, Sort = 1)] public Guid OwnerId { get; set; }
        [CmsField(Label = "Target", Interface = FieldInterface.Uuid, Sort = 2)] public Guid TargetId { get; set; }
    }

    [SugarTable("kdc_owner_ok")]
    [CmsCollection("Owner ok", Name = "kdcOwnerOk")]
    private sealed class OwnerOk : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("kdc_owner_parent_bad")]
    [CmsCollection("Owner parent bad", Name = "kdcOwnerParentBad")]
    private sealed class OwnerWithUnknownParentKey : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(Junction), "NoSuchOwnerId", nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("kdc_owner_target_bad")]
    [CmsCollection("Owner target bad", Name = "kdcOwnerTargetBad")]
    private sealed class OwnerWithUnknownTargetKey : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), "NoSuchTargetId")]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("kdc_owner_shared")]
    [CmsCollection("Owner shared", Name = "kdcOwnerShared")]
    private sealed class OwnerSharingTheJunction : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> First { get; set; } = [];

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Second { get; set; } = [];
    }

    [SugarTable("kdc_owner_shared_bad")]
    [CmsCollection("Owner shared bad", Name = "kdcOwnerSharedBad")]
    private sealed class OwnerSharingAnUnknownKey : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(Junction), "NoSuchOwnerId", nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> First { get; set; } = [];

        [Navigate(typeof(Junction), "NoSuchOwnerId", nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Second { get; set; } = [];
    }

    [SugarTable("kdc_owner_inherited")]
    [CmsCollection("Owner inherited", Name = "kdcOwnerInherited")]
    private sealed class OwnerOfInheritedJunctionKeys : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(JunctionWithInheritedKeys), nameof(JunctionKeysBase.OwnerId), nameof(JunctionKeysBase.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("kdc_owner_case")]
    [CmsCollection("Owner case", Name = "kdcOwnerCase")]
    private sealed class OwnerNamingKeysInOtherCase : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(Junction), "ownerid", "TARGETID")]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("kdc_self")]
    [CmsCollection("Self", Name = "kdcSelf")]
    private sealed class SelfReferencingManyToMany : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(Junction), nameof(Junction.OwnerId), nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<SelfReferencingManyToMany> Related { get; set; } = [];
    }

    [SugarTable("kdc_owner_collection_ok")]
    [CmsCollection("Owner collection ok", Name = "kdcOwnerCollectionOk")]
    private sealed class OwnerOfJunctionCollection : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(JunctionCollection), nameof(JunctionCollection.OwnerId), nameof(JunctionCollection.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("kdc_owner_collection_bad")]
    [CmsCollection("Owner collection bad", Name = "kdcOwnerCollectionBad")]
    private sealed class OwnerOfJunctionCollectionWithUnknownKey : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(JunctionCollection), "NoSuchOwnerId", nameof(JunctionCollection.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("kdc_owner_two_keys")]
    [CmsCollection("Owner two keys", Name = "kdcOwnerTwoKeys")]
    private sealed class OwnerOfJunctionCollectionWithTwoPrimaryKeys : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(JunctionCollectionWithTwoPrimaryKeys), nameof(JunctionCollectionWithTwoPrimaryKeys.OwnerId), nameof(JunctionCollectionWithTwoPrimaryKeys.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [Fact]
    public void An_unknown_junction_parent_key_names_the_relation_the_key_and_the_junction()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(OwnerWithUnknownParentKey), typeof(Target)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("OwnerWithUnknownParentKey.Tags").And.Contain("junction parent key 'NoSuchOwnerId'")
            .And.Contain("Junction has no property with that name")
            .And.Contain("Name a property of Junction that holds the key, or add 'NoSuchOwnerId' to Junction");
    }

    [Fact]
    public void An_unknown_junction_target_key_names_the_relation_the_key_and_the_junction()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(OwnerWithUnknownTargetKey), typeof(Target)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("OwnerWithUnknownTargetKey.Tags").And.Contain("junction target key 'NoSuchTargetId'")
            .And.Contain("Junction has no property with that name")
            .And.Contain("Name a property of Junction that holds the key, or add 'NoSuchTargetId' to Junction");
    }

    [Fact]
    public void An_unknown_key_of_a_junction_shared_by_two_relations_is_refused() =>
        FluentActions.Invoking(() => MetadataScanner.ScanTypes([typeof(OwnerSharingAnUnknownKey), typeof(Target)]))
            .Should().Throw<MetadataException>().Which.Message.Should().Contain("OwnerSharingAnUnknownKey.First").And.Contain("'NoSuchOwnerId'");

    [Fact]
    public void An_unknown_key_of_a_junction_that_is_a_collection_is_refused_by_the_writable_key_check()
    {
        var scan = () => MetadataScanner.ScanTypes(
            [typeof(OwnerOfJunctionCollectionWithUnknownKey), typeof(Target), typeof(JunctionCollection)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("Junction collection 'kdcJunctionCollection'").And.Contain("'NoSuchOwnerId' and 'TargetId'")
            .And.Contain("as writable [CmsField]s");
    }

    [Fact]
    public void A_junction_collection_with_two_primary_keys_is_refused_by_the_junction_primary_key_check()
    {
        var scan = () => MetadataScanner.ScanTypes(
            [typeof(OwnerOfJunctionCollectionWithTwoPrimaryKeys), typeof(Target), typeof(JunctionCollectionWithTwoPrimaryKeys)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("Junction type 'JunctionCollectionWithTwoPrimaryKeys'").And.Contain("exactly one");
    }

    [Fact]
    public void Junction_keys_that_exist_pass()
    {
        MetadataScanner.ScanTypes([typeof(OwnerOk), typeof(Target)]).Should().HaveCount(2);
        MetadataScanner.ScanTypes([typeof(OwnerOfJunctionCollection), typeof(Target), typeof(JunctionCollection)]).Should().HaveCount(3);
    }

    [Fact]
    public void Junction_keys_declared_on_a_base_class_of_the_junction_pass() =>
        MetadataScanner.ScanTypes([typeof(OwnerOfInheritedJunctionKeys), typeof(Target)]).Should().HaveCount(2);

    [Fact]
    public void Junction_keys_that_differ_only_in_case_pass() =>
        MetadataScanner.ScanTypes([typeof(OwnerNamingKeysInOtherCase), typeof(Target)]).Should().HaveCount(2);

    [Fact]
    public void A_junction_shared_by_two_relations_passes() =>
        MetadataScanner.ScanTypes([typeof(OwnerSharingTheJunction), typeof(Target)]).Should().HaveCount(2);

    [Fact]
    public void A_self_referencing_many_to_many_passes() =>
        MetadataScanner.ScanTypes([typeof(SelfReferencingManyToMany)]).Should().ContainSingle();

    // ── primary key ──────────────────────────────────────────────────────────

    [SugarTable("kdc_no_key")]
    [CmsCollection("No key", Name = "kdcNoKey")]
    private sealed class WithoutPrimaryKey
    {
        public string Name { get; set; } = "";
    }

    [SugarTable("kdc_two_keys")]
    [CmsCollection("Two keys", Name = "kdcTwoKeys")]
    private sealed class WithTwoPrimaryKeys
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsPrimaryKey = true)] public Guid Code { get; set; }
        public string Name { get; set; } = "";
    }

    [SugarTable("kdc_three_keys")]
    [CmsCollection("Three keys", Name = "kdcThreeKeys")]
    private sealed class WithThreePrimaryKeys
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsPrimaryKey = true)] public Guid Code { get; set; }
        [SugarColumn(IsPrimaryKey = true)] public Guid Region { get; set; }
    }

    [SugarTable("kdc_inherited_key")]
    [CmsCollection("Inherited key", Name = "kdcInheritedKey")]
    private sealed class WithOverriddenId : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    }

    [SugarTable("kdc_named_key")]
    [CmsCollection("Named key", Name = "kdcNamedKey")]
    private sealed class WithPrimaryKeyNotNamedId
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Code { get; set; }
    }

    [SugarTable("kdc_unmarked_id")]
    [CmsCollection("Unmarked id", Name = "kdcUnmarkedId")]
    private sealed class WithUnmarkedId
    {
        public Guid Id { get; set; }
    }

    [SugarTable("kdc_to_no_key")]
    [CmsCollection("To no key", Name = "kdcToNoKey")]
    private sealed class PointsAtCollectionWithoutPrimaryKey
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public int TargetId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)] public WithoutPrimaryKey? Target { get; set; }
    }

    [SugarTable("kdc_missing_both")]
    [CmsCollection("Missing both", Name = "kdcMissingBoth")]
    private sealed class OwnerWithUnknownKeyAndNoPrimaryKey
    {
        [Navigate(typeof(Junction), "NoSuchOwnerId", nameof(Junction.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<Target> Tags { get; set; } = [];
    }

    [Fact]
    public void A_collection_without_a_primary_key_property_names_the_class_and_both_ways_to_declare_one()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(WithoutPrimaryKey)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("WithoutPrimaryKey").And.Contain("kdcNoKey").And.Contain("no primary key property")
            .And.Contain("[SugarColumn(IsPrimaryKey = true)]").And.Contain("property named Id");
    }

    [Fact]
    public void A_collection_with_two_primary_key_properties_names_both()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(WithTwoPrimaryKeys)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("WithTwoPrimaryKeys").And.Contain("Id, Code").And.Contain("has one primary key property");
    }

    [Fact]
    public void A_collection_with_three_primary_key_properties_names_all_of_them()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(WithThreePrimaryKeys)]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().Contain("Id, Code, Region");
    }

    [Fact]
    public void A_target_without_a_primary_key_property_is_refused_before_any_key_is_compared()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(PointsAtCollectionWithoutPrimaryKey), typeof(WithoutPrimaryKey)]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().Contain("WithoutPrimaryKey").And.Contain("no primary key property");
    }

    [Fact]
    public void A_junction_key_that_does_not_exist_is_reported_before_a_missing_primary_key()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(OwnerWithUnknownKeyAndNoPrimaryKey), typeof(Target)]);

        scan.Should().Throw<MetadataException>().Which.Message.Should().Contain("junction parent key 'NoSuchOwnerId'");
    }

    [Fact]
    public void A_primary_key_that_is_inherited_overridden_or_not_named_Id_passes()
    {
        MetadataScanner.ScanTypes([typeof(WithOverriddenId)]).Should().ContainSingle();
        MetadataScanner.ScanTypes([typeof(WithPrimaryKeyNotNamedId)]).Should().ContainSingle();
    }

    [Fact]
    public void An_Id_property_without_the_primary_key_attribute_passes() =>
        MetadataScanner.ScanTypes([typeof(WithUnmarkedId)]).Should().ContainSingle();

    // ── the scan of whole assemblies ─────────────────────────────────────────

    [Fact]
    public void The_framework_and_fixture_collections_pass_the_checks() =>
        MetadataScanner.Scan(typeof(Struo.Tests.Fixtures.FxArticle).Assembly, typeof(Struo.Infrastructure.Files.File).Assembly)
            .Should().NotBeEmpty();
}
