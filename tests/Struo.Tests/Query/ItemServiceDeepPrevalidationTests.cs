// tests/Struo.Tests/Query/ItemServiceDeepPrevalidationTests.cs
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Struo.Tests.Support.Seeding;
using Xunit;

namespace Struo.Tests.Query;

// An over-cap `deep` request is refused before any root row is read.
public sealed class ItemServiceDeepPrevalidationTests : IDisposable
{
    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();

    private (ItemService Service, CountingItemRepository Repo) Build(StruoQueryOptions options)
    {
        var db = SqlSugarClientFactory.Create(_file.Options(), new TestCurrentUserAccessor(Guid.Empty));
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
        LanguageSeeder.SeedAsync(db, TestLocalization.Default).GetAwaiter().GetResult();

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
            ["article"] = typeof(Article),
            ["category"] = typeof(Category),
            ["tag"] = typeof(Tag),
            ["file"] = typeof(Struo.Infrastructure.Files.File),
            ["user"] = typeof(Struo.Infrastructure.Identity.User),
            ["role"] = typeof(Struo.Infrastructure.Identity.Role),
            ["userRole"] = typeof(Struo.Infrastructure.Identity.UserRole),
            ["mediafolder"] = typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var real = new SqlSugarItemRepository(db, registry, graph, provider, options);
        var repo = new CountingItemRepository(real);
        var expander = new RelationExpander(repo, graph, options);
        var user = new TestCurrentUserAccessor(Guid.Empty);
        var service = TestItemService.Create(
            repo,
            provider,
            registry,
            graph,
            expander,
            graph,
            new LanguageProvider(db),
            user,
            new SqlSugarRevisionStore(db, user),
            new RevisionSnapshotBuilder(repo, provider, registry, graph),
            options: options);
        return (service, repo);
    }

    private static DeepSpec Relations(params string[] names) =>
        new(names.ToDictionary(n => n, _ => new DeepRelationSpec(null, null), StringComparer.OrdinalIgnoreCase));

    [Fact]
    public async Task Query_over_MaxDeepRelations_reads_no_root_rows()
    {
        var (svc, repo) = Build(new StruoQueryOptions { MaxDeepRelations = 1 });
        var query = new QueryModel(null, null, [], 20, 0, null) { Deep = Relations("category", "tags") };

        var act = async () => await svc.QueryAsync("article", query);

        await act.Should().ThrowAsync<QueryException>();
        repo.RootReads.Should().Be(0);
    }

    [Fact]
    public async Task Get_over_MaxDeepRelations_reads_no_root_row()
    {
        var (svc, repo) = Build(new StruoQueryOptions { MaxDeepRelations = 1 });

        var act = async () => await svc.GetAsync("article", Guid.NewGuid().ToString(),
            Relations("category", "tags"));

        await act.Should().ThrowAsync<QueryException>();
        repo.RootReads.Should().Be(0);
    }

    public void Dispose() => _file.Dispose();
}
