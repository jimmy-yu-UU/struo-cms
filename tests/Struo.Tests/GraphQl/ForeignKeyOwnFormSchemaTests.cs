// tests/Struo.Tests/GraphQl/ForeignKeyOwnFormSchemaTests.cs
using System.Text.RegularExpressions;
using AwesomeAssertions;
using HotChocolate.Execution;
using HotChocolate.Serialization;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Api.GraphQl;
using Struo.Application.Metadata;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// A ManyToOne foreign key that is also a writable <c>[CmsField]</c> appears once in the create, update and
/// filter inputs of its collection, in the form the field declares (its scalar and its filter operators). A
/// key without <c>[CmsField]</c>, or one the field rule leaves out, keeps the relation form (<c>ID</c> and
/// <c>IdFilter</c>). The schema is built through the real scanner and <c>StruoTypeModule</c>.
/// </summary>
public sealed partial class ForeignKeyOwnFormSchemaTests
{
    [SugarTable("fk_schema_guid_target")]
    [CmsCollection("FkSchemaGuidTarget", Name = "fkSchemaGuidTarget")]
    public sealed class GuidTarget
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    }

    [SugarTable("fk_schema_int_target")]
    [CmsCollection("FkSchemaIntTarget", Name = "fkSchemaIntTarget")]
    public sealed class IntTarget
    {
        [SugarColumn(IsPrimaryKey = true)] public int Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    }

    [SugarTable("fk_schema_long_target")]
    [CmsCollection("FkSchemaLongTarget", Name = "fkSchemaLongTarget")]
    public sealed class LongTarget
    {
        [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    }

    [SugarTable("fk_schema_string_target")]
    [CmsCollection("FkSchemaStringTarget", Name = "fkSchemaStringTarget")]
    public sealed class StringTarget
    {
        [SugarColumn(IsPrimaryKey = true)] public string Id { get; set; } = "";
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";
    }

    [SugarTable("fk_schema_uuid_owner")]
    [CmsCollection("FkSchemaUuidOwner", Name = "fkSchemaUuidOwner")]
    public sealed class UuidOwner
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "OwnerId", Interface = FieldInterface.Uuid)]
        public Guid? OwnerId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(OwnerId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public GuidTarget? Owner { get; set; }
    }

    [SugarTable("fk_schema_int_owner")]
    [CmsCollection("FkSchemaIntOwner", Name = "fkSchemaIntOwner")]
    public sealed class IntOwner
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "OwnerId", Interface = FieldInterface.Number)]
        public int? OwnerId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(OwnerId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public IntTarget? Owner { get; set; }
    }

    [SugarTable("fk_schema_long_owner")]
    [CmsCollection("FkSchemaLongOwner", Name = "fkSchemaLongOwner")]
    public sealed class LongOwner
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "OwnerId", Interface = FieldInterface.Number)]
        public long? OwnerId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(OwnerId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public LongTarget? Owner { get; set; }
    }

    [SugarTable("fk_schema_text_owner")]
    [CmsCollection("FkSchemaTextOwner", Name = "fkSchemaTextOwner")]
    public sealed class TextOwner
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "OwnerId", Interface = FieldInterface.Text)]
        public string? OwnerId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(OwnerId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public StringTarget? Owner { get; set; }
    }

    [SugarTable("fk_schema_plain_owner")]
    [CmsCollection("FkSchemaPlainOwner", Name = "fkSchemaPlainOwner")]
    public sealed class PlainOwner
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";

        [SugarColumn(IsNullable = true)] public Guid? OwnerId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(OwnerId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public GuidTarget? Owner { get; set; }
    }

    [SugarTable("fk_schema_hidden_owner")]
    [CmsCollection("FkSchemaHiddenOwner", Name = "fkSchemaHiddenOwner")]
    public sealed class HiddenOwner
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "OwnerId", Interface = FieldInterface.Hidden)]
        public Guid? OwnerId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(OwnerId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public GuidTarget? Owner { get; set; }
    }

    [SugarTable("fk_schema_flag_hidden_owner")]
    [CmsCollection("FkSchemaFlagHiddenOwner", Name = "fkSchemaFlagHiddenOwner")]
    public sealed class FlagHiddenOwner
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "OwnerId", Interface = FieldInterface.Uuid, Hidden = true)]
        public Guid? OwnerId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(OwnerId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public GuidTarget? Owner { get; set; }
    }

    [SugarTable("fk_schema_read_only_owner")]
    [CmsCollection("FkSchemaReadOnlyOwner", Name = "fkSchemaReadOnlyOwner")]
    public sealed class ReadOnlyOwner
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "OwnerId", Interface = FieldInterface.Uuid, ReadOnly = true)]
        public Guid? OwnerId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(OwnerId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public GuidTarget? Owner { get; set; }
    }

    [SugarTable("fk_schema_audit_owner")]
    [CmsCollection("FkSchemaAuditOwner", Name = "fkSchemaAuditOwner")]
    public sealed class AuditOwner : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";

        [Navigate(NavigateType.ManyToOne, nameof(CreatedBy))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public GuidTarget? Creator { get; set; }
    }

    [SugarTable("fk_schema_read_only_int_owner")]
    [CmsCollection("FkSchemaReadOnlyIntOwner", Name = "fkSchemaReadOnlyIntOwner")]
    public sealed class ReadOnlyIntOwner
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "OwnerId", Interface = FieldInterface.Number, ReadOnly = true)]
        public int? OwnerId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(OwnerId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public IntTarget? Owner { get; set; }
    }

    [SugarTable("fk_schema_read_only_text_owner")]
    [CmsCollection("FkSchemaReadOnlyTextOwner", Name = "fkSchemaReadOnlyTextOwner")]
    public sealed class ReadOnlyTextOwner
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "OwnerId", Interface = FieldInterface.Text, ReadOnly = true)]
        public string? OwnerId { get; set; }

        [Navigate(NavigateType.ManyToOne, nameof(OwnerId))]
        [CmsRelation(Interface = RelationInterface.Dropdown)]
        [SugarColumn(IsIgnore = true)]
        public StringTarget? Owner { get; set; }
    }

    private static readonly Lazy<Task<string>> Sdl = new(BuildSdlAsync);

    private static async Task<string> BuildSdlAsync()
    {
        Type[] types =
        [
            typeof(GuidTarget), typeof(IntTarget), typeof(LongTarget), typeof(StringTarget),
            typeof(UuidOwner), typeof(IntOwner), typeof(LongOwner), typeof(TextOwner),
            typeof(PlainOwner), typeof(HiddenOwner), typeof(FlagHiddenOwner), typeof(ReadOnlyOwner),
            typeof(AuditOwner), typeof(ReadOnlyIntOwner), typeof(ReadOnlyTextOwner),
        ];
        var collections = MetadataScanner.ScanTypes(types);

        var executor = await new ServiceCollection()
            .AddSingleton<IMetadataProvider>(new CachedMetadataProvider(collections))
            .AddSingleton<IEntityRegistry>(new EntityRegistry(MetadataScanner.ScanDescriptors(types)))
            .AddSingleton<IM2MDescriptorSource>(
                new RelationshipGraph(collections, MetadataScanner.CollectionTypeMap(types)))
            .AddScoped<IGraphQlDataSource, FakeGraphQlDataSource>()
            .AddSingleton<StruoTypeModule>()
            .AddGraphQLServer()
            .AddStruoSchemaTypes()
            .BuildRequestExecutorAsync();

        return SchemaFormatter.FormatAsString(executor.Schema);
    }

    [GeneratedRegex(@"^\s+(?<name>\w+):\s*(?<type>\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex FieldLine();

    // The fields of one input type in the printed schema, name to type text.
    private static async Task<Dictionary<string, string>> InputAsync(string typeName)
    {
        var sdl = await Sdl.Value;
        var block = Regex.Match(sdl, $@"input {typeName} \{{(?<body>.*?)\n\}}", RegexOptions.Singleline);
        block.Success.Should().BeTrue($"the schema has input {typeName}");
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match line in FieldLine().Matches(block.Groups["body"].Value))
            fields.Add(line.Groups["name"].Value, line.Groups["type"].Value);
        return fields;
    }

    [Theory]
    [InlineData("FkSchemaUuidOwner", "ID")]
    [InlineData("FkSchemaIntOwner", "Int")]
    [InlineData("FkSchemaLongOwner", "Long")]
    [InlineData("FkSchemaTextOwner", "String")]
    public async Task A_writable_key_field_is_in_the_create_and_update_inputs_once_with_its_own_type(string owner, string scalar)
    {
        (await InputAsync($"{owner}CreateInput")).Should().Contain("ownerId", scalar);
        (await InputAsync($"{owner}UpdateInput")).Should().Contain("ownerId", scalar);
    }

    [Theory]
    [InlineData("FkSchemaUuidOwner", "IdFilter")]
    [InlineData("FkSchemaIntOwner", "IntFilter")]
    [InlineData("FkSchemaLongOwner", "LongFilter")]
    [InlineData("FkSchemaTextOwner", "StringFilter")]
    public async Task A_writable_key_field_is_in_the_filter_input_once_with_its_own_operators(string owner, string filter)
    {
        var fields = await InputAsync($"{owner}FilterInput");

        fields.Should().Contain("ownerId", filter);
        fields.Should().ContainKey("owner");
    }

    [Theory]
    [InlineData("FkSchemaPlainOwner")]
    [InlineData("FkSchemaHiddenOwner")]
    public async Task A_key_that_is_not_a_writable_field_keeps_the_relation_form(string owner)
    {
        (await InputAsync($"{owner}CreateInput")).Should().Contain("ownerId", "ID");
        (await InputAsync($"{owner}UpdateInput")).Should().Contain("ownerId", "ID");
        (await InputAsync($"{owner}FilterInput")).Should().Contain("ownerId", "IdFilter");
    }

    [Fact]
    public async Task A_key_whose_field_sets_Hidden_is_in_no_input_by_name_and_its_relation_stays_in_the_filter_input()
    {
        (await InputAsync("FkSchemaFlagHiddenOwnerCreateInput")).Should().NotContainKey("ownerId");
        (await InputAsync("FkSchemaFlagHiddenOwnerUpdateInput")).Should().NotContainKey("ownerId");
        var filter = await InputAsync("FkSchemaFlagHiddenOwnerFilterInput");
        filter.Should().NotContainKey("ownerId");
        filter.Should().ContainKey("owner");
    }

    [Fact]
    public async Task A_read_only_key_is_in_neither_write_input_and_is_in_the_filter_input_once()
    {
        (await InputAsync("FkSchemaReadOnlyOwnerCreateInput")).Should().NotContainKey("ownerId");
        (await InputAsync("FkSchemaReadOnlyOwnerUpdateInput")).Should().NotContainKey("ownerId");
        (await InputAsync("FkSchemaReadOnlyOwnerFilterInput")).Should().Contain("ownerId", "IdFilter");
    }

    [Fact]
    public async Task An_audit_key_relation_is_in_the_filter_input_once_and_in_neither_write_input()
    {
        (await InputAsync("FkSchemaAuditOwnerFilterInput")).Should().Contain("createdBy", "StringFilter");
        (await InputAsync("FkSchemaAuditOwnerCreateInput")).Should().NotContainKey("createdBy");
        (await InputAsync("FkSchemaAuditOwnerUpdateInput")).Should().NotContainKey("createdBy");
    }

    [Theory]
    [InlineData("FkSchemaReadOnlyOwner", "IdFilter")]
    [InlineData("FkSchemaReadOnlyIntOwner", "IntFilter")]
    [InlineData("FkSchemaReadOnlyTextOwner", "StringFilter")]
    public async Task A_read_only_key_is_filterable_in_the_form_its_field_declares_and_not_writable(string owner, string filter)
    {
        (await InputAsync($"{owner}FilterInput")).Should().Contain("ownerId", filter);
        (await InputAsync($"{owner}CreateInput")).Should().NotContainKey("ownerId");
        (await InputAsync($"{owner}UpdateInput")).Should().NotContainKey("ownerId");
    }
}
