// tests/Struo.Tests/Query/ItemServicePermissionTests.cs
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Application.Security;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Security;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public class ItemServicePermissionTests : IDisposable
{
    // A permission service that denies reads on a specific collection.
    private sealed class DenyReadPermissions : IPermissionService
    {
        public bool CanRead(string collection) => collection != "article";
        public bool CanWrite(string collection) => true;
        public bool CanDelete(string collection) => true;
        public IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all) => all.ToList();
    }

    // A permission service that denies writes on a specific collection.
    private sealed class DenyWritePermissions : IPermissionService
    {
        public bool CanRead(string collection) => true;
        public bool CanWrite(string collection) => collection != "article";
        public bool CanDelete(string collection) => true;
        public IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all) => all.ToList();
    }

    // A permission service that denies deletes on a specific collection.
    private sealed class DenyDeletePermissions : IPermissionService
    {
        public bool CanRead(string collection) => true;
        public bool CanWrite(string collection) => true;
        public bool CanDelete(string collection) => collection != "article";
        public IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all) => all.ToList();
    }

    // A non-super-admin that holds write/delete on EVERY collection — the privilege-escalation
    // attacker for H3: a delegated grant on the identity tables, but not super-admin.
    private sealed class GrantAllNonSuperAdmin : IPermissionService
    {
        public bool CanRead(string collection) => true;
        public bool CanWrite(string collection) => true;
        public bool CanDelete(string collection) => true;
        public bool IsSuperAdmin => false;
        public IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all) => all.ToList();
    }

    // A super-admin: passes the AdminOnly gate.
    private sealed class SuperAdminPermissions : IPermissionService
    {
        public bool CanRead(string collection) => true;
        public bool CanWrite(string collection) => true;
        public bool CanDelete(string collection) => true;
        public bool IsSuperAdmin => true;
        public IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all) => all.ToList();
    }

    private readonly SqliteTestDatabase _file = new();

    private ItemService BuildService(IPermissionService permissions)
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<Article>();
        db.CodeFirst.InitTables<ArticleTranslation>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Tag>();
        db.CodeFirst.InitTables<ArticleTag>();
        db.CodeFirst.InitTables<Category>();
        db.CodeFirst.InitTables<Struo.Infrastructure.Identity.User>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var scanTypes = new[]
        {
            typeof(Article), typeof(Category), typeof(Tag),
            typeof(Struo.Infrastructure.Files.File), typeof(Struo.Infrastructure.Identity.User),
        };
        var collections = MetadataScanner.ScanTypes(scanTypes);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(scanTypes));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"]  = typeof(Article),
            ["category"] = typeof(Category),
            ["tag"]      = typeof(Tag),
            ["file"]     = typeof(Struo.Infrastructure.Files.File),
            ["user"]     = typeof(Struo.Infrastructure.Identity.User),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph);
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        return new ItemService(repo, provider, registry, permissions,
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer());
    }

    public void Dispose() => _file.Dispose();

    [Fact]
    public async Task Denied_read_QueryAsync_throws_PermissionDeniedException()
    {
        var svc = BuildService(new DenyReadPermissions());
        var act = async () => await svc.QueryAsync("article", new QueryModel(null, null, [], 20, 0, null));
        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    [Fact]
    public async Task Denied_read_GetAsync_throws_PermissionDeniedException()
    {
        var svc = BuildService(new DenyReadPermissions());
        var act = async () => await svc.GetAsync("article", "some-id");
        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    [Fact]
    public async Task Denied_write_CreateAsync_throws_PermissionDeniedException()
    {
        var svc = BuildService(new DenyWritePermissions());
        using var body = System.Text.Json.JsonDocument.Parse("""{"status":"draft","translations":{"en":{"title":"Hello"}}}""");
        var act = async () => await svc.CreateAsync("article", body.RootElement);
        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    [Fact]
    public async Task Denied_write_UpdateAsync_throws_PermissionDeniedException()
    {
        var svc = BuildService(new DenyWritePermissions());
        using var body = System.Text.Json.JsonDocument.Parse("""{"status":"published"}""");
        var act = async () => await svc.UpdateAsync("article", "some-id", body.RootElement);
        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    [Fact]
    public async Task Denied_delete_DeleteAsync_throws_PermissionDeniedException()
    {
        var svc = BuildService(new DenyDeletePermissions());
        var act = async () => await svc.DeleteAsync("article", "some-id");
        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    // H3: an AdminOnly collection (user) rejects create/update/delete from a non-super-admin,
    // even though the caller holds a write/delete grant on every collection. Prevents a delegated
    // grant on the identity tables from being escalated into super-admin.
    [Fact]
    public async Task AdminOnly_create_denied_for_non_superadmin_with_write_grant()
    {
        var svc = BuildService(new GrantAllNonSuperAdmin());
        using var body = System.Text.Json.JsonDocument.Parse("""{"email":"x@y.z","isActive":true}""");
        var act = async () => await svc.CreateAsync("user", body.RootElement);
        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    [Fact]
    public async Task AdminOnly_update_denied_for_non_superadmin_with_write_grant()
    {
        var svc = BuildService(new GrantAllNonSuperAdmin());
        using var body = System.Text.Json.JsonDocument.Parse("""{"isActive":false}""");
        var act = async () => await svc.UpdateAsync("user", System.Guid.NewGuid().ToString(), body.RootElement);
        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    [Fact]
    public async Task AdminOnly_delete_denied_for_non_superadmin_with_delete_grant()
    {
        var svc = BuildService(new GrantAllNonSuperAdmin());
        var act = async () => await svc.DeleteAsync("user", System.Guid.NewGuid().ToString());
        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    // H3: a super-admin passes the AdminOnly gate (create succeeds; no PermissionDeniedException).
    [Fact]
    public async Task AdminOnly_create_allowed_for_superadmin()
    {
        var svc = BuildService(new SuperAdminPermissions());
        using var body = System.Text.Json.JsonDocument.Parse("""{"email":"admin@example.com","isActive":true}""");
        var act = async () => await svc.CreateAsync("user", body.RootElement);
        await act.Should().NotThrowAsync<PermissionDeniedException>();
    }

    // D2: optimistic concurrency. version starts at 0 and increments on each successful update.
    [Fact]
    public async Task Version_increments_on_update()
    {
        var svc = BuildService(new SuperAdminPermissions());
        using var create = System.Text.Json.JsonDocument.Parse("""{"name":"A"}""");
        var created = await svc.CreateAsync("category", create.RootElement);
        var id = created["id"]!.ToString()!;
        created["version"].Should().Be(0L);

        using var upd = System.Text.Json.JsonDocument.Parse("""{"name":"B","version":0}""");
        var updated = await svc.UpdateAsync("category", id, upd.RootElement);
        updated!["version"].Should().Be(1L);
    }

    // D2: a stale version (someone else already updated) is rejected with a 409-mapped conflict.
    [Fact]
    public async Task Stale_version_update_throws_conflict()
    {
        var svc = BuildService(new SuperAdminPermissions());
        using var create = System.Text.Json.JsonDocument.Parse("""{"name":"A"}""");
        var id = (await svc.CreateAsync("category", create.RootElement))["id"]!.ToString()!;

        // First writer moves version 0 → 1.
        using var first = System.Text.Json.JsonDocument.Parse("""{"name":"B","version":0}""");
        await svc.UpdateAsync("category", id, first.RootElement);

        // Second writer still holds the stale version 0 → conflict.
        using var stale = System.Text.Json.JsonDocument.Parse("""{"name":"C","version":0}""");
        var act = async () => await svc.UpdateAsync("category", id, stale.RootElement);
        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    // D2: an update that omits version stays backward compatible (no conflict, still increments).
    [Fact]
    public async Task Update_without_version_still_succeeds_and_increments()
    {
        var svc = BuildService(new SuperAdminPermissions());
        using var create = System.Text.Json.JsonDocument.Parse("""{"name":"A"}""");
        var id = (await svc.CreateAsync("category", create.RootElement))["id"]!.ToString()!;

        using var upd = System.Text.Json.JsonDocument.Parse("""{"name":"B"}""");
        var updated = await svc.UpdateAsync("category", id, upd.RootElement);
        updated!["version"].Should().Be(1L);
    }
}
