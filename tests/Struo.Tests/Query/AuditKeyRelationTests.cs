using Struo.Tests.Support.Seeding;
using System.Text.Json;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// A ManyToOne relation whose foreign key is an audit field (<c>CreatedBy</c>) never lets a body write
/// that key: a create stores what the audit stamping gives, and an update and a revert leave the stored
/// value as it was.
/// </summary>
public sealed class AuditKeyRelationTests : IDisposable
{
    private const string Collection = "auditKeyItem";

    private static readonly Guid ActorGuid = Guid.Parse("55555555-5555-5555-5555-555555555555");
    private static readonly Guid OtherGuid = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid StoredGuid = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [SugarTable("audit_key_item")]
    [CmsCollection("AuditKeyItem", Name = Collection, Revisions = true)]
    public sealed class Item : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Name", Interface = FieldInterface.Text)] public string Name { get; set; } = "";

        [Navigate(NavigateType.ManyToOne, nameof(CreatedBy))]
        [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Name}")]
        [SugarColumn(IsIgnore = true)]
        public Role? Creator { get; set; }
    }

    [SugarTable("audit_key_item")]
    public sealed class StoredRow
    {
        [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }
        public Guid? CreatedBy { get; set; }
    }

    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();
    private readonly ISqlSugarClient _db;
    private readonly ItemService _service;

    public AuditKeyRelationTests()
    {
        var user = new TestCurrentUserAccessor(ActorGuid);
        _db = SqlSugarClientFactory.Create(_file.Options(), user);
        _db.CodeFirst.InitTables<Item>();
        _db.CodeFirst.InitTables<Role>();
        _db.CodeFirst.InitTables<Language>();
        _db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(_db, TestLocalization.Default).GetAwaiter().GetResult();

        var types = new[] { typeof(Item), typeof(Role) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var graph = new RelationshipGraph(collections, new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            [Collection] = typeof(Item),
            ["role"] = typeof(Role),
        });
        var repo = new SqlSugarItemRepository(_db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, new StruoQueryOptions());
        var revisions = new SqlSugarRevisionStore(_db, user);
        var snapshots = new RevisionSnapshotBuilder(repo, provider, registry, graph);
        _service = TestItemService.Create(
            repo, provider, registry, graph, expander, graph, new LanguageProvider(_db), user, revisions, snapshots);
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement Body(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }

    private async Task<Guid?> StoredCreatedByAsync(Guid id) =>
        (await _db.Queryable<StoredRow>().Where(r => r.Id == id).FirstAsync()).CreatedBy;

    private async Task<Guid> CreateAsync(string json)
    {
        var created = await _service.CreateAsync(Collection, Body(json));
        return Guid.Parse(created["id"]!.ToString()!);
    }

    private async Task StoreCreatedByAsync(Guid id, Guid value)
    {
        await _db.Updateable<Item>().SetColumns(i => i.CreatedBy == value).Where(i => i.Id == id).ExecuteCommandAsync();
    }

    [Fact]
    public async Task A_create_stores_the_audit_stamp_whatever_the_body_gives_the_key()
    {
        var id = await CreateAsync("""{"name":"n","createdBy":"22222222-2222-2222-2222-222222222222"}""");

        (await StoredCreatedByAsync(id)).Should().Be(ActorGuid);
    }

    [Fact]
    public async Task An_update_body_cannot_change_the_audit_key()
    {
        var id = await CreateAsync("""{"name":"n"}""");
        await StoreCreatedByAsync(id, StoredGuid);

        await _service.UpdateAsync(Collection, id.ToString(), Body("{\"createdBy\":\"" + OtherGuid + "\"}"));

        (await StoredCreatedByAsync(id)).Should().Be(StoredGuid);
    }

    [Fact]
    public async Task A_revert_does_not_change_the_audit_key()
    {
        var id = await CreateAsync("""{"name":"n"}""");
        await _service.UpdateAsync(Collection, id.ToString(), Body("""{"name":"m"}"""));
        await StoreCreatedByAsync(id, StoredGuid);

        var reverted = await _service.RevertAsync(Collection, id.ToString(), 1);

        reverted.Should().NotBeNull();
        (await StoredCreatedByAsync(id)).Should().Be(StoredGuid);
    }
}
