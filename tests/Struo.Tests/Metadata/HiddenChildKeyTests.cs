using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// A one-to-many relation lists the child rows by the child's key, and a reader cannot name a key whose field sets
/// <c>Hidden = true</c>. The scan accepts such a key only when the child declares a many-to-one relation back to
/// the parent on it, which the list filters through, and refuses it otherwise.
/// </summary>
public sealed class HiddenChildKeyTests
{
    [SugarTable("hck_other")]
    [CmsCollection("Hck other", Name = "hckOther")]
    public sealed class Other
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
    }

    [SugarTable("hck_bare_parent")]
    [CmsCollection("Hck bare parent", Name = "hckBareParent")]
    public sealed class BareParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(BareChild.ParentId))]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)]
        public List<BareChild> Kids { get; set; } = [];
    }

    [SugarTable("hck_bare_child")]
    [CmsCollection("Hck bare child", Name = "hckBareChild")]
    public sealed class BareChild
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Parent", Interface = FieldInterface.Uuid, Hidden = true)]
        public Guid? ParentId { get; set; }
    }

    [SugarTable("hck_back_parent")]
    [CmsCollection("Hck back parent", Name = "hckBackParent")]
    public sealed class BackParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(BackChild.ParentId))]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)]
        public List<BackChild> Kids { get; set; } = [];
    }

    [SugarTable("hck_back_child")]
    [CmsCollection("Hck back child", Name = "hckBackChild")]
    public sealed class BackChild
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Parent", Interface = FieldInterface.Uuid, Hidden = true)]
        public Guid? ParentId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(ParentId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public BackParent? Parent { get; set; }
    }

    [SugarTable("hck_elsewhere_parent")]
    [CmsCollection("Hck elsewhere parent", Name = "hckElsewhereParent")]
    public sealed class ElsewhereParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(ElsewhereChild.ParentId))]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)]
        public List<ElsewhereChild> Kids { get; set; } = [];
    }

    // The relation on the hidden key points at another collection, so it cannot list the parent's children.
    [SugarTable("hck_elsewhere_child")]
    [CmsCollection("Hck elsewhere child", Name = "hckElsewhereChild")]
    public sealed class ElsewhereChild
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Parent", Interface = FieldInterface.Uuid, Hidden = true)]
        public Guid? ParentId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(ParentId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public Other? Parent { get; set; }
    }

    [SugarTable("hck_visible_parent")]
    [CmsCollection("Hck visible parent", Name = "hckVisibleParent")]
    public sealed class VisibleParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(VisibleChild.ParentId))]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)]
        public List<VisibleChild> Kids { get; set; } = [];
    }

    [SugarTable("hck_visible_child")]
    [CmsCollection("Hck visible child", Name = "hckVisibleChild")]
    public sealed class VisibleChild
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Parent", Interface = FieldInterface.Uuid)]
        public Guid? ParentId { get; set; }
    }

    [SugarTable("hck_face_parent")]
    [CmsCollection("Hck face parent", Name = "hckFaceParent")]
    public sealed class FaceParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(FaceChild.ParentId))]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)]
        public List<FaceChild> Kids { get; set; } = [];
    }

    // The flag is unset: a key whose interface is Hidden stays nameable by a reader.
    [SugarTable("hck_face_child")]
    [CmsCollection("Hck face child", Name = "hckFaceChild")]
    public sealed class FaceChild
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Parent", Interface = FieldInterface.Hidden)]
        public Guid? ParentId { get; set; }
    }

    [SugarTable("hck_plain_parent")]
    [CmsCollection("Hck plain parent", Name = "hckPlainParent")]
    public sealed class PlainParent
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [Navigate(NavigateType.OneToMany, nameof(PlainChild.ParentId))]
        [CmsRelation(Interface = RelationInterface.RelatedList)]
        [SugarColumn(IsIgnore = true)]
        public List<PlainChild> Kids { get; set; } = [];
    }

    // The key has no [CmsField], so it is not a field at all.
    [SugarTable("hck_plain_child")]
    [CmsCollection("Hck plain child", Name = "hckPlainChild")]
    public sealed class PlainChild
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [SugarColumn(IsNullable = true)] public Guid? ParentId { get; set; }
    }

    [Fact]
    public void A_hidden_child_key_with_no_relation_back_is_refused_naming_both_declarations_and_both_ways_out()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(BareParent), typeof(BareChild)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("Relation 'BareParent.Kids'")
            .And.Contain("'BareChild.ParentId'")
            .And.Contain("Hidden = true")
            .And.Contain("Unknown field")
            .And.Contain("many-to-one [CmsRelation] on 'BareChild.ParentId' that points at 'BareParent'")
            .And.Contain("remove Hidden = true from 'BareChild.ParentId'");
    }

    [Fact]
    public void A_hidden_child_key_whose_relation_points_at_another_collection_is_refused()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(Other), typeof(ElsewhereParent), typeof(ElsewhereChild)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("Relation 'ElsewhereParent.Kids'").And.Contain("'ElsewhereChild.ParentId'");
    }

    [Fact]
    public void A_hidden_child_key_with_a_many_to_one_relation_back_to_the_parent_is_accepted()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(BackParent), typeof(BackChild)]);

        scan.Should().NotThrow();
    }

    [Theory]
    [InlineData(typeof(VisibleParent), typeof(VisibleChild))]
    [InlineData(typeof(FaceParent), typeof(FaceChild))]
    [InlineData(typeof(PlainParent), typeof(PlainChild))]
    public void A_child_key_a_reader_can_name_is_accepted_without_a_relation_back(Type parent, Type child)
    {
        var scan = () => MetadataScanner.ScanTypes([parent, child]);

        scan.Should().NotThrow();
    }
}
