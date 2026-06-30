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

    private readonly SqliteTestDatabase _file = new();

    private ItemService BuildService(IPermissionService permissions)
    {
        var db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<Article>();
        db.CodeFirst.InitTables<ArticleTranslation>();
        db.CodeFirst.InitTables<Language>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var collections = MetadataScanner.ScanTypes(
            [typeof(Article), typeof(Category), typeof(Struo.Infrastructure.Files.File)]);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(
            [typeof(Article), typeof(Category), typeof(Struo.Infrastructure.Files.File)]));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"]  = typeof(Article),
            ["category"] = typeof(Category),
            ["file"]     = typeof(Struo.Infrastructure.Files.File),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph);
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        return new ItemService(repo, provider, registry, permissions,
            graph, expander, graph, resolver, languages, new StruoQueryOptions());
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
}
