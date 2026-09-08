// tests/Struo.Tests/Query/ItemServiceUpdateRequiredSemanticsTests.cs
using System.Text.Json;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;
using Struo.Infrastructure.Identity;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// A PUT is a merge (<see cref="ItemService.UpdateCoreAsync"/> only overlays fields the client
/// actually sent), so since 2026-09-08 a Required field a PUT body omits keeps its stored value
/// instead of 400ing. The Required check itself is unchanged: it still runs, just against the
/// merged entity rather than the freshly-parsed body, so an explicit null/blank still 400s. The
/// create path is untouched throughout — a Required field must still be sent on create.
/// </summary>
public sealed class ItemServiceUpdateRequiredSemanticsTests : IDisposable
{
    // One Required field, one not — enough to distinguish "omitted" from "explicitly cleared".
    [SugarTable("update_required_thing")]
    [CmsCollection("UpdateRequiredThing")]
    public sealed class RequiredThing : AuditableEntity
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

        [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true)]
        public string Name { get; set; } = string.Empty;

        [SugarColumn(IsNullable = true)]
        [CmsField(Label = "Note", Interface = FieldInterface.Text)]
        public string? Note { get; set; }
    }

    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceUpdateRequiredSemanticsTests()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<RequiredThing>();
        db.CodeFirst.InitTables<UserRole>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(RequiredThing), typeof(UserRole) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["requiredThing"] = typeof(RequiredThing),
            ["userRole"] = typeof(UserRole),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        var revisionUser = new TestCurrentUserAccessor(Guid.Empty);
        var revisionStore = new SqlSugarRevisionStore(db, revisionUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            revisionUser, revisionStore, snapshotBuilder, new NoopUserSessionRevocationService());
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement Body(string json) => JsonDocument.Parse(json).RootElement;

    [Fact]
    public async Task Create_still_rejects_a_missing_required_field()
    {
        var act = () => _svc.CreateAsync("requiredThing", Body("""{"note":"n"}"""));
        await act.Should().ThrowAsync<QueryException>().WithMessage("Field 'name' is required.");
    }

    [Fact]
    public async Task Update_omitting_the_required_field_keeps_the_stored_value()
    {
        var created = await _svc.CreateAsync("requiredThing", Body("""{"name":"Original"}"""));
        var id = created["id"]!.ToString()!;

        var updated = await _svc.UpdateAsync("requiredThing", id, Body("""{"note":"updated note"}"""));

        updated.Should().NotBeNull();
        updated!["name"].Should().Be("Original");
        updated["note"].Should().Be("updated note");
    }

    [Fact]
    public async Task Update_sending_an_explicit_null_for_the_required_field_is_rejected()
    {
        var created = await _svc.CreateAsync("requiredThing", Body("""{"name":"Original"}"""));
        var id = created["id"]!.ToString()!;

        var act = () => _svc.UpdateAsync("requiredThing", id, Body("""{"name":null}"""));

        await act.Should().ThrowAsync<QueryException>().WithMessage("Field 'name' is required.");
    }

    [Fact]
    public async Task Update_sending_whitespace_only_for_the_required_field_is_rejected()
    {
        var created = await _svc.CreateAsync("requiredThing", Body("""{"name":"Original"}"""));
        var id = created["id"]!.ToString()!;

        var act = () => _svc.UpdateAsync("requiredThing", id, Body("""{"name":"   "}"""));

        await act.Should().ThrowAsync<QueryException>().WithMessage("Field 'name' is required.");
    }

    [Fact]
    public async Task Create_without_roleId_is_rejected_with_the_camelCase_field_name()
    {
        var act = () => _svc.CreateAsync("userRole", Body($$"""{"userId":"{{Guid.NewGuid()}}"}"""));
        await act.Should().ThrowAsync<QueryException>().WithMessage("Field 'roleId' is required.");
    }

    [Fact]
    public async Task Update_setting_roleId_to_the_all_zero_guid_is_rejected()
    {
        var created = await _svc.CreateAsync("userRole",
            Body($$"""{"userId":"{{Guid.NewGuid()}}","roleId":"{{Guid.NewGuid()}}"}"""));
        var id = created["id"]!.ToString()!;

        var act = () => _svc.UpdateAsync("userRole", id,
            Body("""{"roleId":"00000000-0000-0000-0000-000000000000"}"""));

        await act.Should().ThrowAsync<QueryException>().WithMessage("Field 'roleId' is required.");
    }
}
