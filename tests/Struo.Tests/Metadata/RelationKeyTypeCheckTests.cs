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
/// A relation's key property must have the type of the primary key it points at, each with
/// <c>Nullable&lt;&gt;</c> removed. The scan refuses any other pairing for the four key
/// positions a <c>[CmsRelation]</c> declares: the many-to-one key, the one-to-many child key and
/// the two many-to-many junction keys.
/// </summary>
public class RelationKeyTypeCheckTests
{
    [SugarTable("rkc_long_target")]
    [CmsCollection("Long target", Name = "rkcLongTarget")]
    private sealed class LongTarget
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    }

    [SugarTable("rkc_guid_target")]
    [CmsCollection("Guid target", Name = "rkcGuidTarget")]
    private sealed class GuidTarget : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    }

    [SugarTable("rkc_string_target")]
    [CmsCollection("String target", Name = "rkcStringTarget")]
    private sealed class StringTarget
    {
        [SugarColumn(IsPrimaryKey = true)] public string Id { get; set; } = "";
    }

    // ── many-to-one ──────────────────────────────────────────────────────────

    [SugarTable("rkc_m2o_bad")]
    [CmsCollection("M2O bad", Name = "rkcM2oBad")]
    private sealed class M2oIntToLong
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public int? TargetId { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)] public LongTarget? Target { get; set; }
    }

    [SugarTable("rkc_m2o_string")]
    [CmsCollection("M2O string", Name = "rkcM2oString")]
    private sealed class M2oStringToGuid
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public string? TargetId { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)] public GuidTarget? Target { get; set; }
    }

    [SugarTable("rkc_m2o_guid")]
    [CmsCollection("M2O guid", Name = "rkcM2oGuid")]
    private sealed class M2oGuidToLong
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid TargetId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)] public LongTarget? Target { get; set; }
    }

    [SugarTable("rkc_m2o_long_nullable")]
    [CmsCollection("M2O long nullable", Name = "rkcM2oLongNullable")]
    private sealed class M2oNullableLongToLong
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public long? TargetId { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)] public LongTarget? Target { get; set; }
    }

    [SugarTable("rkc_m2o_long")]
    [CmsCollection("M2O long", Name = "rkcM2oLong")]
    private sealed class M2oLongToLong
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public long TargetId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)] public LongTarget? Target { get; set; }
    }

    [SugarTable("rkc_m2o_guid_nullable")]
    [CmsCollection("M2O guid nullable", Name = "rkcM2oGuidNullable")]
    private sealed class M2oNullableGuidToGuid : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? TargetId { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)] public GuidTarget? Target { get; set; }
    }

    [SugarTable("rkc_m2o_string_ok")]
    [CmsCollection("M2O string ok", Name = "rkcM2oStringOk")]
    private sealed class M2oStringToString
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public string? TargetId { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)] public StringTarget? Target { get; set; }
    }

    [SugarTable("rkc_tree_node")]
    [CmsCollection("Tree node", Name = "rkcTreeNode")]
    private sealed class TreeNode : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? ParentId { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(ParentId))]
        [CmsRelation(Interface = RelationInterface.TreeSelect)]
        [SugarColumn(IsIgnore = true)] public TreeNode? Parent { get; set; }
    }

    [SugarTable("rkc_navigate_only")]
    [CmsCollection("Navigate only", Name = "rkcNavigateOnly")]
    private sealed class NavigateWithoutCmsRelation
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public int? TargetId { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [SugarColumn(IsIgnore = true)] public LongTarget? Target { get; set; }
    }

    [Fact]
    public void Many_to_one_key_of_another_type_names_both_sides()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(M2oIntToLong), typeof(LongTarget)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("M2oIntToLong.Target").And.Contain("M2oIntToLong.TargetId").And.Contain("an int? property")
            .And.Contain("LongTarget.Id").And.Contain("a long property").And.Contain("Declare 'M2oIntToLong.TargetId' as long?")
            .And.Contain("exists with another type needs a migration");
    }

    private abstract class RelationDeclaredOnABase
    {
        [SugarColumn(IsNullable = true)] public int? TargetId { get; set; }

        [Navigate(NavigateType.OneToOne, nameof(TargetId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)] public LongTarget? Target { get; set; }
    }

    [SugarTable("rkc_subclass")]
    [CmsCollection("Subclass", Name = "rkcSubclass")]
    private sealed class CollectionInheritingARelation : RelationDeclaredOnABase
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [Fact]
    public void The_message_names_the_class_that_declares_the_relation()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(CollectionInheritingARelation), typeof(LongTarget)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith("Relation 'RelationDeclaredOnABase.Target'").And.NotContain("CollectionInheritingARelation");
    }

    [Fact]
    public void Many_to_one_string_key_to_a_guid_key_is_refused()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(M2oStringToGuid), typeof(GuidTarget)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("M2oStringToGuid.Target").And.Contain("M2oStringToGuid.TargetId").And.Contain("a string property")
            .And.Contain("GuidTarget.Id").And.Contain("a Guid property");
    }

    [Fact]
    public void Many_to_one_guid_key_to_a_long_key_is_refused()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(M2oGuidToLong), typeof(LongTarget)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("M2oGuidToLong.Target").And.Contain("M2oGuidToLong.TargetId").And.Contain("a Guid property")
            .And.Contain("LongTarget.Id").And.Contain("a long property")
            .And.Contain("Declare 'M2oGuidToLong.TargetId' as long:");
    }

    [Fact]
    public void Many_to_one_key_of_the_primary_key_type_passes_whether_or_not_it_is_nullable()
    {
        MetadataScanner.ScanTypes([typeof(M2oLongToLong), typeof(LongTarget)]).Should().HaveCount(2);
        MetadataScanner.ScanTypes([typeof(M2oNullableLongToLong), typeof(LongTarget)]).Should().HaveCount(2);
        MetadataScanner.ScanTypes([typeof(M2oNullableGuidToGuid), typeof(GuidTarget)]).Should().HaveCount(2);
        MetadataScanner.ScanTypes([typeof(M2oStringToString), typeof(StringTarget)]).Should().HaveCount(2);
    }

    [Fact]
    public void A_self_reference_with_a_key_of_the_primary_key_type_passes() =>
        MetadataScanner.ScanTypes([typeof(TreeNode)]).Should().ContainSingle();

    [Fact]
    public void A_navigate_without_cms_relation_is_not_checked() =>
        MetadataScanner.ScanTypes([typeof(NavigateWithoutCmsRelation), typeof(LongTarget)]).Should().HaveCount(2);

    // ── one-to-many ──────────────────────────────────────────────────────────

    [SugarTable("rkc_child_int")]
    [CmsCollection("Child int", Name = "rkcChildInt")]
    private sealed class ChildIntKey
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public int? ParentId { get; set; }
    }

    [SugarTable("rkc_child_long")]
    [CmsCollection("Child long", Name = "rkcChildLong")]
    private sealed class ChildLongKey
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public long? ParentId { get; set; }
    }

    [SugarTable("rkc_parent_bad")]
    [CmsCollection("Parent bad", Name = "rkcParentBad")]
    private sealed class OneToManyBadParent
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(ChildIntKey.ParentId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)] public List<ChildIntKey> Children { get; set; } = [];
    }

    [SugarTable("rkc_parent_ok")]
    [CmsCollection("Parent ok", Name = "rkcParentOk")]
    private sealed class OneToManyOkParent
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(ChildLongKey.ParentId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)] public List<ChildLongKey> Children { get; set; } = [];
    }

    [Fact]
    public void One_to_many_child_key_of_another_type_names_both_sides()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(OneToManyBadParent), typeof(ChildIntKey)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("OneToManyBadParent.Children").And.Contain("ChildIntKey.ParentId").And.Contain("an int? property")
            .And.Contain("OneToManyBadParent.Id").And.Contain("a long property").And.Contain("Declare 'ChildIntKey.ParentId' as long?");
    }

    [Fact]
    public void One_to_many_child_key_of_the_primary_key_type_passes() =>
        MetadataScanner.ScanTypes([typeof(OneToManyOkParent), typeof(ChildLongKey)]).Should().HaveCount(2);

    [SugarTable("rkc_parent_case_bad")]
    [CmsCollection("Parent case bad", Name = "rkcParentCaseBad")]
    private sealed class OneToManyBadParentNamingTheKeyInOtherCase
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }

        [Navigate(NavigateType.OneToMany, "parentid")]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)] public List<ChildIntKey> Children { get; set; } = [];
    }

    [SugarTable("rkc_parent_case_ok")]
    [CmsCollection("Parent case ok", Name = "rkcParentCaseOk")]
    private sealed class OneToManyOkParentNamingTheKeyInOtherCase
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }

        [Navigate(NavigateType.OneToMany, "parentid")]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)] public List<ChildLongKey> Children { get; set; } = [];
    }

    [Fact]
    public void One_to_many_child_key_in_another_letter_case_is_refused_whatever_its_type()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(OneToManyOkParentNamingTheKeyInOtherCase), typeof(ChildLongKey)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("OneToManyOkParentNamingTheKeyInOtherCase.Children").And.Contain("foreign key 'parentid'")
            .And.Contain("ChildLongKey has no property 'parentid'; it has 'ParentId'").And.Contain("key names are case-sensitive");
    }

    [Fact]
    public void One_to_many_child_key_in_another_letter_case_is_refused_as_a_name_before_its_type_is_compared()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(OneToManyBadParentNamingTheKeyInOtherCase), typeof(ChildIntKey)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("ChildIntKey has no property 'parentid'; it has 'ParentId'").And.NotContain("primary key");
    }

    // ── relations typed with a plain class named like a collection ───────────

    private static class PlainScope
    {
        public sealed class RkcNamedTarget
        {
            public long Id { get; set; }
        }

        public sealed class RkcKidLongKey
        {
            public Guid? ParentId { get; set; }
        }

        public sealed class RkcKidGuidKey
        {
            public long? ParentId { get; set; }
        }
    }

    [SugarTable("rkc_named_target")]
    [CmsCollection("Named target")]
    private sealed class RkcNamedTarget
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [SugarTable("rkc_jct_long_target")]
    private sealed class JunctionLongTarget
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid ParentId { get; set; }
        public long TargetId { get; set; }
    }

    [SugarTable("rkc_jct_guid_target")]
    private sealed class JunctionGuidTarget
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid ParentId { get; set; }
        public Guid TargetId { get; set; }
    }

    [SugarTable("rkc_plain_mm_bad")]
    [CmsCollection("Plain MM bad", Name = "rkcPlainMmBad")]
    private sealed class ManyToManyOverPlainClassBadTargetKey
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(JunctionLongTarget), nameof(JunctionLongTarget.ParentId), nameof(JunctionLongTarget.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<PlainScope.RkcNamedTarget> Tags { get; set; } = [];
    }

    [SugarTable("rkc_plain_mm_ok")]
    [CmsCollection("Plain MM ok", Name = "rkcPlainMmOk")]
    private sealed class ManyToManyOverPlainClassWithTheCollectionKeyType
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(typeof(JunctionGuidTarget), nameof(JunctionGuidTarget.ParentId), nameof(JunctionGuidTarget.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<PlainScope.RkcNamedTarget> Tags { get; set; } = [];
    }

    [Fact]
    public void A_junction_target_key_is_compared_with_the_primary_key_of_the_collection_a_plain_element_class_is_named_after()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(ManyToManyOverPlainClassBadTargetKey), typeof(RkcNamedTarget)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith("Relation 'ManyToManyOverPlainClassBadTargetKey.Tags'")
            .And.Contain("'JunctionLongTarget.TargetId', a long property")
            .And.Contain("the primary key 'RkcNamedTarget.Id', a Guid property");
    }

    [Fact]
    public void A_junction_target_key_of_the_collection_key_type_passes_whatever_the_plain_element_class_declares() =>
        MetadataScanner.ScanTypes([typeof(ManyToManyOverPlainClassWithTheCollectionKeyType), typeof(RkcNamedTarget)])
            .Should().HaveCount(2);

    [SugarTable("rkc_kid_long_key")]
    [CmsCollection("Kid long key")]
    private sealed class RkcKidLongKey
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public long? ParentId { get; set; }
    }

    [SugarTable("rkc_kid_guid_key")]
    [CmsCollection("Kid guid key")]
    private sealed class RkcKidGuidKey
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? ParentId { get; set; }
    }

    [SugarTable("rkc_plain_kid_bad")]
    [CmsCollection("Plain kid bad", Name = "rkcPlainKidBad")]
    private sealed class OneToManyOverPlainClassWhoseCollectionDeclaresAnotherKeyType
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, "ParentId")]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)] public List<PlainScope.RkcKidLongKey> Kids { get; set; } = [];
    }

    [SugarTable("rkc_plain_kid_ok")]
    [CmsCollection("Plain kid ok", Name = "rkcPlainKidOk")]
    private sealed class OneToManyOverPlainClassWhoseCollectionDeclaresTheRightKeyType
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, "ParentId")]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)] public List<PlainScope.RkcKidGuidKey> Kids { get; set; } = [];
    }

    [Fact]
    public void A_child_key_is_compared_as_the_collection_class_declares_it_not_as_the_plain_element_class_does()
    {
        var scan = () => MetadataScanner.ScanTypes(
            [typeof(OneToManyOverPlainClassWhoseCollectionDeclaresAnotherKeyType), typeof(RkcKidLongKey)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().StartWith("Relation 'OneToManyOverPlainClassWhoseCollectionDeclaresAnotherKeyType.Kids'")
            .And.Contain("'RkcKidLongKey.ParentId', a long? property")
            .And.Contain("the primary key 'OneToManyOverPlainClassWhoseCollectionDeclaresAnotherKeyType.Id', a Guid property")
            .And.Contain("Declare 'RkcKidLongKey.ParentId' as Guid?");
    }

    [Fact]
    public void A_child_key_of_the_type_the_collection_class_declares_passes_whatever_the_plain_element_class_declares() =>
        MetadataScanner.ScanTypes([typeof(OneToManyOverPlainClassWhoseCollectionDeclaresTheRightKeyType), typeof(RkcKidGuidKey)])
            .Should().HaveCount(2);

    // ── many-to-many ─────────────────────────────────────────────────────────

    [SugarTable("rkc_jct_parent_bad")]
    private sealed class JunctionIntParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public int ParentId { get; set; }
        public long TargetId { get; set; }
    }

    [SugarTable("rkc_jct_target_bad")]
    private sealed class JunctionIntTarget
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public long ParentId { get; set; }
        public int TargetId { get; set; }
    }

    [SugarTable("rkc_jct_ok")]
    private sealed class JunctionLongBoth
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public long ParentId { get; set; }
        public long TargetId { get; set; }
    }

    [SugarTable("rkc_jct_nullable")]
    private sealed class JunctionNullableLongBoth
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public long? ParentId { get; set; }
        public long? TargetId { get; set; }
    }

    [SugarTable("rkc_mm_parent_bad")]
    [CmsCollection("MM parent bad", Name = "rkcMmParentBad")]
    private sealed class ManyToManyBadParentKey
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }

        [Navigate(typeof(JunctionIntParent), nameof(JunctionIntParent.ParentId), nameof(JunctionIntParent.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<LongTarget> Tags { get; set; } = [];
    }

    [SugarTable("rkc_mm_target_bad")]
    [CmsCollection("MM target bad", Name = "rkcMmTargetBad")]
    private sealed class ManyToManyBadTargetKey
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }

        [Navigate(typeof(JunctionIntTarget), nameof(JunctionIntTarget.ParentId), nameof(JunctionIntTarget.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<LongTarget> Tags { get; set; } = [];
    }

    [SugarTable("rkc_mm_ok")]
    [CmsCollection("MM ok", Name = "rkcMmOk")]
    private sealed class ManyToManyOk
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }

        [Navigate(typeof(JunctionLongBoth), nameof(JunctionLongBoth.ParentId), nameof(JunctionLongBoth.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<LongTarget> Tags { get; set; } = [];
    }

    [SugarTable("rkc_mm_nullable")]
    [CmsCollection("MM nullable", Name = "rkcMmNullable")]
    private sealed class ManyToManyNullableKeys
    {
        [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }

        [Navigate(typeof(JunctionNullableLongBoth), nameof(JunctionNullableLongBoth.ParentId), nameof(JunctionNullableLongBoth.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)] public List<LongTarget> Tags { get; set; } = [];
    }

    [Fact]
    public void Many_to_many_parent_key_of_another_type_names_both_sides()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(ManyToManyBadParentKey), typeof(LongTarget)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("ManyToManyBadParentKey.Tags").And.Contain("JunctionIntParent.ParentId").And.Contain("an int property")
            .And.Contain("ManyToManyBadParentKey.Id").And.Contain("a long property")
            .And.Contain("Declare 'JunctionIntParent.ParentId' as long:");
    }

    [Fact]
    public void Many_to_many_target_key_of_another_type_names_both_sides()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(ManyToManyBadTargetKey), typeof(LongTarget)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("ManyToManyBadTargetKey.Tags").And.Contain("JunctionIntTarget.TargetId").And.Contain("an int property")
            .And.Contain("LongTarget.Id").And.Contain("a long property").And.Contain("Declare 'JunctionIntTarget.TargetId' as long");
    }

    [Fact]
    public void Many_to_many_keys_of_the_primary_key_types_pass_whether_or_not_they_are_nullable()
    {
        MetadataScanner.ScanTypes([typeof(ManyToManyOk), typeof(LongTarget)]).Should().HaveCount(2);
        MetadataScanner.ScanTypes([typeof(ManyToManyNullableKeys), typeof(LongTarget)]).Should().HaveCount(2);
    }

    // ── what is not compared ─────────────────────────────────────────────────

    [Fact]
    public void A_target_that_is_not_in_the_scan_is_not_compared() =>
        MetadataScanner.ScanTypes([typeof(M2oIntToLong)]).Should().ContainSingle();

    // ── the scan of whole assemblies ─────────────────────────────────────────

    [Fact]
    public void The_fixture_and_framework_collections_pass_the_check() =>
        MetadataScanner.Scan(typeof(Struo.Tests.Fixtures.FxArticle).Assembly, typeof(Struo.Infrastructure.Files.File).Assembly)
            .Should().NotBeEmpty();
}
