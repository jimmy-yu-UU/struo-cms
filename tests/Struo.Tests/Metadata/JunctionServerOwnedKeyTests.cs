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
/// A junction collection's keys are set by the request body, so a key the body never sets (a <c>ReadOnly</c>
/// field, an audit field or a soft-delete column) is refused at startup, whether or not a many-to-one
/// relation names it as its foreign key.
/// </summary>
public sealed class JunctionServerOwnedKeyTests
{
    [SugarTable("jsk_target")]
    [CmsCollection("Target", Name = "jskTarget")]
    private sealed class Target : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    }

    [SugarTable("jsk_read_only_owner")]
    [CmsCollection("Read only owner", Name = "jskReadOnlyOwner")]
    private sealed class ReadOnlyKeyOwner : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(ReadOnlyKeyLink), nameof(ReadOnlyKeyLink.OwnerId), nameof(ReadOnlyKeyLink.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)]
        public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("jsk_read_only_link")]
    [CmsCollection("Read only link", Name = "jskReadOnlyLink")]
    private sealed class ReadOnlyKeyLink
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [CmsField(Label = "Owner", Interface = FieldInterface.Uuid, ReadOnly = true)]
        public Guid OwnerId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(OwnerId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public ReadOnlyKeyOwner? Owner { get; set; }

        [CmsField(Label = "Target", Interface = FieldInterface.Uuid)]
        public Guid TargetId { get; set; }
    }

    [SugarTable("jsk_audit_owner")]
    [CmsCollection("Audit owner", Name = "jskAuditOwner")]
    private sealed class AuditKeyOwner : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(AuditKeyLink), nameof(AuditKeyLink.CreatedBy), nameof(AuditKeyLink.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)]
        public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("jsk_audit_link")]
    [CmsCollection("Audit link", Name = "jskAuditLink")]
    private sealed class AuditKeyLink : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(CreatedBy))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public AuditKeyOwner? Owner { get; set; }

        [CmsField(Label = "Target", Interface = FieldInterface.Uuid)]
        public Guid TargetId { get; set; }
    }

    [SugarTable("jsk_soft_delete_owner")]
    [CmsCollection("Soft delete owner", Name = "jskSoftDeleteOwner")]
    private sealed class SoftDeleteKeyOwner : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(SoftDeleteKeyLink), nameof(SoftDeleteKeyLink.DeletedBy), nameof(SoftDeleteKeyLink.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)]
        public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("jsk_soft_delete_link")]
    [CmsCollection("Soft delete link", Name = "jskSoftDeleteLink")]
    private sealed class SoftDeleteKeyLink : ISoftDeletable
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public DateTime? DeletedAt { get; set; }
        public Guid? DeletedBy { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(DeletedBy))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public SoftDeleteKeyOwner? Owner { get; set; }

        [CmsField(Label = "Target", Interface = FieldInterface.Uuid)]
        public Guid TargetId { get; set; }
    }

    [SugarTable("jsk_writable_owner")]
    [CmsCollection("Writable owner", Name = "jskWritableOwner")]
    private sealed class WritableKeyOwner : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(WritableKeyLink), nameof(WritableKeyLink.OwnerId), nameof(WritableKeyLink.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)]
        public List<Target> Tags { get; set; } = [];
    }

    // The owner key is a plain column that only a many-to-one relation names; the body sets it through the relation.
    [SugarTable("jsk_writable_link")]
    [CmsCollection("Writable link", Name = "jskWritableLink")]
    private sealed class WritableKeyLink
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid OwnerId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(OwnerId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public WritableKeyOwner? Owner { get; set; }

        [CmsField(Label = "Target", Interface = FieldInterface.Uuid)]
        public Guid TargetId { get; set; }
    }

    [SugarTable("jsk_hidden_owner")]
    [CmsCollection("Hidden owner", Name = "jskHiddenOwner")]
    private sealed class HiddenKeyOwner : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(HiddenKeyLink), nameof(HiddenKeyLink.OwnerId), nameof(HiddenKeyLink.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)]
        public List<Target> Tags { get; set; } = [];
    }

    [SugarTable("jsk_hidden_link")]
    [CmsCollection("Hidden link", Name = "jskHiddenLink", Hidden = true)]
    private sealed class HiddenKeyLink
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [CmsField(Label = "Owner", Interface = FieldInterface.Uuid, Hidden = true)]
        public Guid OwnerId { get; set; }

        [CmsField(Label = "Target", Interface = FieldInterface.Uuid, Hidden = true)]
        public Guid TargetId { get; set; }
    }

    [SugarTable("jsk_hidden_one_owner")]
    [CmsCollection("Hidden one owner", Name = "jskHiddenOneOwner")]
    private sealed class HiddenOneKeyOwner : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(HiddenOneKeyLink), nameof(HiddenOneKeyLink.OwnerId), nameof(HiddenOneKeyLink.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)]
        public List<Target> Tags { get; set; } = [];
    }

    // The owner key is also named by a many-to-one relation; its field is hidden.
    [SugarTable("jsk_hidden_one_link")]
    [CmsCollection("Hidden one link", Name = "jskHiddenOneLink")]
    private sealed class HiddenOneKeyLink
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [CmsField(Label = "Owner", Interface = FieldInterface.Uuid, Hidden = true)]
        public Guid OwnerId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(OwnerId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public HiddenOneKeyOwner? Owner { get; set; }

        [CmsField(Label = "Target", Interface = FieldInterface.Uuid)]
        public Guid TargetId { get; set; }
    }

    [SugarTable("jsk_face_owner")]
    [CmsCollection("Face owner", Name = "jskFaceOwner")]
    private sealed class HiddenInterfaceOwner : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [Navigate(typeof(HiddenInterfaceLink), nameof(HiddenInterfaceLink.OwnerId), nameof(HiddenInterfaceLink.TargetId))]
        [CmsRelation(Interface = RelationInterface.TagSelect)]
        [SugarColumn(IsIgnore = true)]
        public List<Target> Tags { get; set; } = [];
    }

    // The flag is unset: a key whose interface is Hidden stays nameable, so the junction is accepted.
    [SugarTable("jsk_face_link")]
    [CmsCollection("Face link", Name = "jskFaceLink")]
    private sealed class HiddenInterfaceLink
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

        [CmsField(Label = "Owner", Interface = FieldInterface.Hidden)]
        public Guid OwnerId { get; set; }

        [CmsField(Label = "Target", Interface = FieldInterface.Uuid)]
        public Guid TargetId { get; set; }
    }

    // A case name, the junction collection and the key the refusal names.
    public static TheoryData<string, string, string> Refused => new()
    {
        { nameof(ReadOnlyKeyLink), "jskReadOnlyLink", "OwnerId" },
        { nameof(AuditKeyLink), "jskAuditLink", "CreatedBy" },
        { nameof(SoftDeleteKeyLink), "jskSoftDeleteLink", "DeletedBy" },
    };

    private static Type[] TypesOf(string junctionClass) => junctionClass switch
    {
        nameof(ReadOnlyKeyLink) => [typeof(ReadOnlyKeyOwner), typeof(Target), typeof(ReadOnlyKeyLink)],
        nameof(AuditKeyLink) => [typeof(AuditKeyOwner), typeof(Target), typeof(AuditKeyLink)],
        _ => [typeof(SoftDeleteKeyOwner), typeof(Target), typeof(SoftDeleteKeyLink)],
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void A_junction_key_the_body_never_sets_is_refused_at_startup(string junctionClass, string junction, string key)
    {
        var scan = () => MetadataScanner.ScanTypes(TypesOf(junctionClass));

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain($"Junction collection '{junction}'").And.Contain($"'{junctionClass}.{key}'")
            .And.Contain("a request body never sets").And.Contain("writable [CmsField]");
    }

    [Fact]
    public void A_junction_collection_with_both_keys_Hidden_is_refused_naming_both_and_the_way_out()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(HiddenKeyOwner), typeof(Target), typeof(HiddenKeyLink)]);

        var message = scan.Should().Throw<MetadataException>().Which.Message;
        message.Should().Contain("Junction collection 'jskHiddenLink' (used by 'jskHiddenOwner.tags')")
            .And.Contain("'HiddenKeyLink.OwnerId' and 'HiddenKeyLink.TargetId' are Hidden = true")
            .And.Contain("the GraphQL inputs do not list")
            .And.Contain("remove Hidden = true")
            .And.Contain("[CmsCollection(Hidden = true)]");
    }

    [Fact]
    public void A_junction_key_that_a_many_to_one_relation_also_names_is_refused_when_its_field_is_Hidden()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(HiddenOneKeyOwner), typeof(Target), typeof(HiddenOneKeyLink)]);

        scan.Should().Throw<MetadataException>().Which.Message
            .Should().Contain("'HiddenOneKeyLink.OwnerId' is Hidden = true").And.NotContain("TargetId' is");
    }

    [Fact]
    public void A_junction_key_whose_interface_is_Hidden_with_the_flag_unset_is_accepted()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(HiddenInterfaceOwner), typeof(Target), typeof(HiddenInterfaceLink)]);

        scan.Should().NotThrow();
    }

    [Fact]
    public void A_junction_key_that_only_a_many_to_one_relation_names_is_accepted()
    {
        var scan = () => MetadataScanner.ScanTypes([typeof(WritableKeyOwner), typeof(Target), typeof(WritableKeyLink)]);

        scan.Should().NotThrow();
    }
}
