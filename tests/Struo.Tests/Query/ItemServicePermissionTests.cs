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
using Struo.Infrastructure.Revisions;
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

    // Holds read on 'article' and nothing else. A relation hop must not become a way to reach
    // rows in a collection the caller has no read grant on: 'deep' drops the relation, a relation
    // filter/sort path is refused outright.
    private sealed class ReadArticleOnlyPermissions : IPermissionService
    {
        public bool CanRead(string collection) =>
            string.Equals(collection, "article", StringComparison.OrdinalIgnoreCase);
        public bool CanWrite(string collection) => true;
        public bool CanDelete(string collection) => true;
        public IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all) => all.ToList();
    }

    // Reads everything except one named collection.
    private sealed class DenyReadOfPermissions(string denied) : IPermissionService
    {
        public bool CanRead(string collection) =>
            !string.Equals(collection, denied, StringComparison.OrdinalIgnoreCase);
        public bool CanWrite(string collection) => true;
        public bool CanDelete(string collection) => true;
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
        db.CodeFirst.InitTables<Struo.Infrastructure.Files.File>();
        db.CodeFirst.InitTables<Struo.Infrastructure.Identity.User>();
        db.CodeFirst.InitTables<Struo.Infrastructure.Identity.Role>();
        db.CodeFirst.InitTables<Struo.Infrastructure.Identity.UserRole>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        // User.Roles (M2M TagSelect) navigates through UserRole to Role, so both must be scanned
        // alongside User or RelationshipGraph construction throws "targets unknown collection".
        var scanTypes = new[]
        {
            typeof(Article), typeof(Category), typeof(Tag),
            typeof(Struo.Infrastructure.Files.File), typeof(Struo.Infrastructure.Identity.User),
            typeof(Struo.Infrastructure.Identity.Role), typeof(Struo.Infrastructure.Identity.UserRole),
            typeof(Struo.Infrastructure.Files.MediaFolder),
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
            ["role"]     = typeof(Struo.Infrastructure.Identity.Role),
            ["userRole"] = typeof(Struo.Infrastructure.Identity.UserRole),
            ["mediafolder"] = typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph, resolver, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        var revisionUser = new TestCurrentUserAccessor(Guid.Empty);
        var revisionStore = new SqlSugarRevisionStore(db, revisionUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);
        return new ItemService(repo, provider, registry, permissions,
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer(),
            revisionUser, revisionStore, snapshotBuilder, new NoopUserSessionRevocationService());
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

    // Seeds one category and one article pointing at it, using a super-admin service. Both services
    // built by BuildService share the same SQLite file, so a narrower caller can read the result back.
    private async Task<string> SeedArticleWithCategoryAsync()
    {
        var admin = BuildService(new SuperAdminPermissions());
        using var cat = System.Text.Json.JsonDocument.Parse("""{"name":"News"}""");
        var catId = (await admin.CreateAsync("category", cat.RootElement))["id"]!.ToString()!;
        using var art = System.Text.Json.JsonDocument.Parse(
            """{"status":"draft","categoryId":"__CAT__","translations":{"en":{"title":"Hello"}}}"""
                .Replace("__CAT__", catId));
        return (await admin.CreateAsync("article", art.RootElement))["id"]!.ToString()!;
    }

    // Inserts a 'file' row directly: ItemService refuses generic creates on the file collection,
    // since the upload pipeline owns StorageKey and the derived columns.
    private Guid SeedFileRow()
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<Struo.Infrastructure.Files.File>();
        var row = new Struo.Infrastructure.Files.File
        {
            Id = Guid.CreateVersion7(),
            StorageKey = "2026/08/deadbeef.pdf",
            FileName = "confidential-salaries.pdf",
            ContentType = "application/pdf",
            Size = 4242,
            Status = "published",
        };
        db.Insertable(row).ExecuteCommand();
        return row.Id;
    }

    private async Task<string> SeedArticleWithOgImageAsync(Guid fileId)
    {
        var admin = BuildService(new SuperAdminPermissions());
        using var art = System.Text.Json.JsonDocument.Parse(
            """{"status":"draft","translations":{"en":{"title":"Hello","seoOgImageId":"__F__"}}}"""
                .Replace("__F__", fileId.ToString()));
        return (await admin.CreateAsync("article", art.RootElement))["id"]!.ToString()!;
    }

    private static IReadOnlyDictionary<string, object?> EnTranslation(
        IReadOnlyDictionary<string, object?> row) =>
        (IReadOnlyDictionary<string, object?>)
            ((System.Collections.IDictionary)row["translations"]!)["en"]!;

    // A translatable Image field resolves into a projected 'file' row. That is a read of the file
    // collection and needs its own grant: without it the nested object must not materialise, or a
    // role granted only 'article' reads file names, sizes and content types it has no grant for.
    [Fact]
    public async Task Translatable_image_field_is_not_resolved_without_read_on_the_file_collection()
    {
        var fileId = SeedFileRow();
        var id = await SeedArticleWithOgImageAsync(fileId);
        var narrow = BuildService(new DenyReadOfPermissions("file"));

        var row = await narrow.GetAsync("article", id, locale: "en");

        var en = EnTranslation(row!);
        en["seoOgImage"].Should().BeNull();
    }

    // The counterpart: with the grant the nested file row is still resolved.
    [Fact]
    public async Task Translatable_image_field_is_resolved_with_read_on_the_file_collection()
    {
        var fileId = SeedFileRow();
        var id = await SeedArticleWithOgImageAsync(fileId);
        var svc = BuildService(new SuperAdminPermissions());

        var row = await svc.GetAsync("article", id, locale: "en");

        var en = EnTranslation(row!);
        en["seoOgImage"].Should().NotBeNull();
        ((IReadOnlyDictionary<string, object?>)en["seoOgImage"]!)["fileName"]
            .Should().Be("confidential-salaries.pdf");
    }

    private static DeepSpec Deep(params string[] relations) =>
        new(relations.ToDictionary(
            r => r, _ => new DeepRelationSpec(null, null), StringComparer.OrdinalIgnoreCase));

    // A caller who cannot read 'category' gets the article without the expanded relation, rather
    // than a 403 — expanding is a convenience, and failing the whole read would take the admin SPA
    // down for any narrowly-granted role (its item form requests deep=category,tags).
    [Fact]
    public async Task Deep_expansion_omits_a_relation_whose_target_is_unreadable()
    {
        var id = await SeedArticleWithCategoryAsync();
        var narrow = BuildService(new ReadArticleOnlyPermissions());

        var row = await narrow.GetAsync("article", id, Deep("category"));

        row.Should().NotBeNull();
        row!.Should().NotContainKey("category");
    }

    // The counterpart that keeps the omission honest: with the grant, the relation is still expanded.
    [Fact]
    public async Task Deep_expansion_includes_a_relation_whose_target_is_readable()
    {
        var id = await SeedArticleWithCategoryAsync();
        var svc = BuildService(new SuperAdminPermissions());

        var row = await svc.GetAsync("article", id, Deep("category"));

        row!.Should().ContainKey("category");
    }

    // A relation filter path is refused, not silently dropped: dropping it would return rows that
    // do not match the filter the caller sent. meta.total on such a query is also a blind-extraction
    // oracle over the unreadable collection.
    [Fact]
    public async Task Relation_filter_path_into_an_unreadable_collection_is_denied()
    {
        var narrow = BuildService(new ReadArticleOnlyPermissions());
        var q = new QueryModel(
            null, new ComparisonFilter("category.name", QueryOperator.Eq, "News"), [], 20, 0, null);

        var act = async () => await narrow.QueryAsync("article", q);

        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    [Fact]
    public async Task Relation_sort_path_into_an_unreadable_collection_is_denied()
    {
        var narrow = BuildService(new ReadArticleOnlyPermissions());
        var q = new QueryModel(null, null, [new SortField("category.name", false)], 20, 0, null);

        var act = async () => await narrow.QueryAsync("article", q);

        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    // The many-to-many hop is the same rule: 'tags' targets 'tag', which this caller cannot read.
    [Fact]
    public async Task Deep_expansion_omits_a_many_to_many_relation_whose_target_is_unreadable()
    {
        var id = await SeedArticleWithCategoryAsync();
        var narrow = BuildService(new ReadArticleOnlyPermissions());

        var row = await narrow.GetAsync("article", id, Deep("tags"));

        row!.Should().NotContainKey("tags");
    }

    // Pruning is recursive. Root 'category' and the first hop ('children' -> category) are both
    // readable; the second hop ('articles' -> article) is not, and must be dropped from the nested
    // rows rather than riding in on the readable hop above it.
    [Fact]
    public async Task Deep_expansion_prunes_an_unreadable_target_at_the_second_hop()
    {
        var admin = BuildService(new SuperAdminPermissions());
        using var parent = System.Text.Json.JsonDocument.Parse("""{"name":"News"}""");
        var parentId = (await admin.CreateAsync("category", parent.RootElement))["id"]!.ToString()!;
        using var child = System.Text.Json.JsonDocument.Parse(
            """{"name":"Sub","parentId":"__P__"}""".Replace("__P__", parentId));
        await admin.CreateAsync("category", child.RootElement);

        var svc = BuildService(new DenyReadOfPermissions("article"));
        var deep = new DeepSpec(new Dictionary<string, DeepRelationSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["children"] = new DeepRelationSpec(null, null, Deep("articles")),
        });

        var row = await svc.GetAsync("category", parentId, deep);

        row!.Should().ContainKey("children");
        var children = ((System.Collections.IEnumerable)row["children"]!)
            .Cast<IReadOnlyDictionary<string, object?>>().ToList();
        children.Should().ContainSingle();
        children[0].Should().NotContainKey("articles");
    }

    // Everything requested is unreadable: the read still succeeds, it just carries no expansion.
    // Pins that the all-pruned early return is a 200, not a 400 or a 403.
    [Fact]
    public async Task Deep_expansion_with_every_relation_unreadable_still_returns_the_item()
    {
        var id = await SeedArticleWithCategoryAsync();
        var narrow = BuildService(new ReadArticleOnlyPermissions());

        var row = await narrow.GetAsync("article", id, Deep("category", "tags"));

        row.Should().NotBeNull();
        row!.Should().ContainKey("id");
        row.Should().NotContainKey("category");
        row.Should().NotContainKey("tags");
    }

    // Dropping an unreadable relation must not swallow the validation of the ones that remain:
    // an unknown relation name is still rejected by name.
    [Fact]
    public async Task Unknown_relation_is_still_rejected_when_an_unreadable_sibling_is_pruned()
    {
        var id = await SeedArticleWithCategoryAsync();
        var narrow = BuildService(new ReadArticleOnlyPermissions());

        var act = async () => await narrow.GetAsync("article", id, Deep("category", "ghostRelation"));

        await act.Should().ThrowAsync<QueryException>();
    }

    // Where the two halves of the design meet: the relation being expanded is readable, but its
    // nested filter reaches on into a collection that is not. The filter half wins — a filter is
    // refused, never silently dropped.
    [Fact]
    public async Task Nested_deep_filter_crossing_into_an_unreadable_collection_is_denied()
    {
        var admin = BuildService(new SuperAdminPermissions());
        using var cat = System.Text.Json.JsonDocument.Parse("""{"name":"News"}""");
        var catId = (await admin.CreateAsync("category", cat.RootElement))["id"]!.ToString()!;

        // 'category' (root) and 'children' -> category are readable; the filter's own hop is not.
        var svc = BuildService(new DenyReadOfPermissions("article"));
        var deep = new DeepSpec(new Dictionary<string, DeepRelationSpec>(StringComparer.OrdinalIgnoreCase)
        {
            ["children"] = new DeepRelationSpec(null, null)
            {
                Filter = new ComparisonFilter("articles.status", QueryOperator.Eq, "draft"),
            },
        });

        var act = async () => await svc.GetAsync("category", catId, deep);

        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    // Optimistic concurrency. version starts at 0 and increments on each successful update.
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

    // A stale version (someone else already updated) is rejected with a 409-mapped conflict.
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

    // An update that omits version stays backward compatible (no conflict, still increments).
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
