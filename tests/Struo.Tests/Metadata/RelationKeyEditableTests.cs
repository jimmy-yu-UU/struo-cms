using System.Text.Json;
using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.Metadata;

/// <summary>
/// A ManyToOne relation is editable only when its foreign key is a field the server writes: a
/// <c>ReadOnly</c> or audit key is never written, so the admin relation picker is not offered for it.
/// </summary>
public sealed class RelationKeyEditableTests
{
    private const string TargetName = "keyEditableTarget";

    [SugarTable("key_editable_target")]
    [CmsCollection("KeyEditableTarget", Name = TargetName)]
    public sealed class Target : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    }

    [SugarTable("key_editable_owner")]
    [CmsCollection("KeyEditableOwner", Name = "keyEditableOwner")]
    public sealed class Owner : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Plain", Interface = FieldInterface.Hidden)]
        public Guid? PlainId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(PlainId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public Target? Plain { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Locked", Interface = FieldInterface.Hidden, ReadOnly = true)]
        public Guid? LockedId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(LockedId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public Target? Locked { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(CreatedBy))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public Target? Creator { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Declined", Interface = FieldInterface.Hidden)]
        public Guid? DeclinedId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(DeclinedId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, Editable = false)]
        [SugarColumn(IsIgnore = true)]
        public Target? Declined { get; set; }

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "LockedDeclined", Interface = FieldInterface.Hidden, ReadOnly = true)]
        public Guid? LockedDeclinedId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(LockedDeclinedId))]
        [CmsRelation(Interface = RelationInterface.Dropdown, Editable = false)]
        [SugarColumn(IsIgnore = true)]
        public Target? LockedDeclined { get; set; }
    }

    private static Dictionary<string, RelationMetadata> ScannedRelations() =>
        MetadataScanner.ScanTypes([typeof(Owner), typeof(Target)])
            .Single(c => c.Name == "keyEditableOwner")
            .Relations.ToDictionary(r => r.Name);

    [Fact]
    public void A_relation_on_an_ordinary_key_is_editable()
    {
        ScannedRelations()["plain"].Editable.Should().BeTrue();
    }

    [Fact]
    public void A_relation_on_a_read_only_key_is_not_editable()
    {
        ScannedRelations()["locked"].Editable.Should().BeFalse();
    }

    [Fact]
    public void A_relation_on_an_audit_key_is_not_editable()
    {
        ScannedRelations()["creator"].Editable.Should().BeFalse();
    }

    [Fact]
    public void A_relation_declared_not_editable_stays_not_editable()
    {
        var relations = ScannedRelations();

        relations["declined"].Editable.Should().BeFalse();
        relations["lockedDeclined"].Editable.Should().BeFalse();
    }

    [Fact]
    public void The_schema_json_of_a_read_only_key_relation_says_editable_false()
    {
        var collection = MetadataScanner.ScanTypes([typeof(Owner), typeof(Target)]).Single(c => c.Name == "keyEditableOwner");

        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(collection, JsonSerializerOptions.Web));
        var editable = doc.RootElement.GetProperty("relations").EnumerateArray()
            .ToDictionary(r => r.GetProperty("name").GetString()!, r => r.GetProperty("editable").GetBoolean());

        editable["plain"].Should().BeTrue();
        editable["locked"].Should().BeFalse();
        editable["creator"].Should().BeFalse();
    }
}
