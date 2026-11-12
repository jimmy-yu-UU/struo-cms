// tests/Struo.Tests/Query/ItemUseCasesDeletedAccessTests.cs
using AwesomeAssertions;
using Struo.Application.Query;
using Struo.Application.Security;
using Struo.Domain.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Struo.Tests.Support.Seeding;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// <see cref="IItemUseCases.QueryAsync"/> and <see cref="IItemUseCases.GetAsync"/> refuse
/// <see cref="DeletedFilter.Only"/> and <see cref="DeletedFilter.With"/> without delete permission, on
/// their own: no controller or resolver gate sits in front of the calls here.
/// </summary>
public sealed class ItemUseCasesDeletedAccessTests : IDisposable
{
    private const string Collection = "fxCategory";
    private const string ReadDeniedMessage = "Read not permitted.";
    private const string DeniedMessage = "Viewing deleted items requires delete permission.";

    private sealed class Grants(bool canDelete, bool canRead = true) : IPermissionService
    {
        public bool CanRead(string collection) => canRead;
        public bool CanWrite(string collection) => false;
        public bool CanDelete(string collection) => canDelete;
        public IReadOnlyCollection<string> ReadableFields(string c, IEnumerable<string> all) => all.ToList();
    }

    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();

    public void Dispose() => _file.Dispose();

    private IItemUseCases Build(bool canDelete, bool canRead = true)
    {
        var db = SqlSugarClientFactory.Create(_file.Options(), new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<FxArticle>();
        db.CodeFirst.InitTables<FxArticleTranslation>();
        db.CodeFirst.InitTables<FxTag>();
        db.CodeFirst.InitTables<FxArticleTag>();
        db.CodeFirst.InitTables<FxCategory>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db, TestLocalization.Default).GetAwaiter().GetResult();

        Type[] types = [typeof(FxArticle), typeof(FxCategory), typeof(FxTag), typeof(Struo.Infrastructure.Files.File), typeof(Struo.Infrastructure.Files.MediaFolder)];
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var graph = new RelationshipGraph(collections, new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["fxArticle"] = typeof(FxArticle),
            ["fxCategory"] = typeof(FxCategory),
            ["fxTag"] = typeof(FxTag),
            ["file"] = typeof(Struo.Infrastructure.Files.File),
            ["mediafolder"] = typeof(Struo.Infrastructure.Files.MediaFolder),
        });
        var options = new Struo.Application.Configuration.StruoQueryOptions();
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, options);
        var user = new TestCurrentUserAccessor(Guid.Empty);
        return TestItemService.Create(
            repo, provider, registry, graph, new RelationExpander(repo, graph, options), graph,
            new LanguageProvider(db), user, new SqlSugarRevisionStore(db, user),
            new RevisionSnapshotBuilder(repo, provider, registry, graph),
            permissions: new Grants(canDelete, canRead));
    }

    private static QueryModel EmptyQuery() => new(null, null, [], 20, 0, null);

    [Theory]
    [InlineData(DeletedFilter.Only)]
    [InlineData(DeletedFilter.With)]
    public async Task QueryAsync_without_delete_permission_is_denied(DeletedFilter deleted)
    {
        var items = Build(canDelete: false);
        var act = async () => await items.QueryAsync(Collection, EmptyQuery(), deleted: deleted);
        (await act.Should().ThrowAsync<PermissionDeniedException>()).WithMessage(DeniedMessage);
    }

    [Theory]
    [InlineData(DeletedFilter.Only)]
    [InlineData(DeletedFilter.With)]
    public async Task GetAsync_without_delete_permission_is_denied(DeletedFilter deleted)
    {
        var items = Build(canDelete: false);
        var act = async () => await items.GetAsync(Collection, Guid.NewGuid().ToString(), deleted: deleted);
        (await act.Should().ThrowAsync<PermissionDeniedException>()).WithMessage(DeniedMessage);
    }

    [Theory]
    [InlineData(DeletedFilter.Only)]
    [InlineData(DeletedFilter.With)]
    public async Task QueryAsync_with_delete_permission_succeeds(DeletedFilter deleted)
    {
        var items = Build(canDelete: true);
        var page = await items.QueryAsync(Collection, EmptyQuery(), deleted: deleted);
        page.Total.Should().Be(0);
    }

    [Theory]
    [InlineData(DeletedFilter.Only)]
    [InlineData(DeletedFilter.With)]
    public async Task GetAsync_with_delete_permission_succeeds(DeletedFilter deleted)
    {
        var items = Build(canDelete: true);
        (await items.GetAsync(Collection, Guid.NewGuid().ToString(), deleted: deleted)).Should().BeNull();
    }

    [Theory]
    [InlineData(DeletedFilter.Only)]
    [InlineData(DeletedFilter.With)]
    public async Task Without_read_or_delete_permission_the_read_denial_comes_first(DeletedFilter deleted)
    {
        var items = Build(canDelete: false, canRead: false);
        var query = async () => await items.QueryAsync(Collection, EmptyQuery(), deleted: deleted);
        (await query.Should().ThrowAsync<PermissionDeniedException>()).WithMessage(ReadDeniedMessage);
        var get = async () => await items.GetAsync(Collection, Guid.NewGuid().ToString(), deleted: deleted);
        (await get.Should().ThrowAsync<PermissionDeniedException>()).WithMessage(ReadDeniedMessage);
    }

    [Fact]
    public async Task Exclude_needs_only_read_permission()
    {
        var items = Build(canDelete: false);
        (await items.QueryAsync(Collection, EmptyQuery())).Total.Should().Be(0);
        (await items.GetAsync(Collection, Guid.NewGuid().ToString())).Should().BeNull();
    }
}
