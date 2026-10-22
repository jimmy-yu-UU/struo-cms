// tests/Struo.Tests/Query/TranslationSyncDeleteTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// A create inserts translation rows under a parent id that is new in its transaction, so it issues
/// no DELETE against the translation table (the DELETE's next-key locks deadlock concurrent creates
/// on InnoDB). An update still replaces the stored row for each supplied locale. Captures the SQL
/// the repository executes, so the guard holds on every backend the default suite runs on.
/// </summary>
public sealed class TranslationSyncDeleteTests : IDisposable
{
    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();
    private readonly ISqlSugarClient _db;
    private readonly SqlSugarItemRepository _repo;
    private readonly List<string> _sql = [];

    public TranslationSyncDeleteTests()
    {
        _db = SqlSugarClientFactory.Create(_file.Options(), new TestCurrentUserAccessor(Guid.Empty));
        _db.CodeFirst.InitTables<ArticleTranslation>();
        var types = new[] { typeof(Article), typeof(Category), typeof(Tag) };
        var collections = MetadataScanner.ScanTypes(types);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var graph = new RelationshipGraph(collections, new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"] = typeof(Article), ["category"] = typeof(Category), ["tag"] = typeof(Tag),
        });
        _repo = new SqlSugarItemRepository(_db, registry, graph, new CachedMetadataProvider(collections), new StruoQueryOptions());
        _db.Aop.OnLogExecuting = (sql, _) => _sql.Add(sql);
    }

    public void Dispose() => _file.Dispose();

    private Task SyncAsync(Guid articleId, string title, bool isCreate) =>
        _repo.SyncTranslationsAsync(
            typeof(ArticleTranslation), nameof(ArticleTranslation.ArticleId), nameof(ArticleTranslation.Locale),
            [nameof(ArticleTranslation.Title)], articleId,
            new Dictionary<string, IReadOnlyDictionary<string, object?>>
            {
                ["en"] = new Dictionary<string, object?> { ["title"] = title },
            },
            isCreate);

    private bool DeletedFromTranslations() =>
        _sql.Any(s => s.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public async Task Create_inserts_without_deleting_from_the_translation_table()
    {
        var id = Guid.CreateVersion7();

        await SyncAsync(id, "First", isCreate: true);

        DeletedFromTranslations().Should().BeFalse();
        (await _db.Queryable<ArticleTranslation>().Where(t => t.ArticleId == id).ToListAsync())
            .Should().ContainSingle().Which.Title.Should().Be("First");
    }

    [Fact]
    public async Task Update_deletes_the_stored_row_then_inserts_the_replacement()
    {
        var id = Guid.CreateVersion7();
        await SyncAsync(id, "First", isCreate: true);
        _sql.Clear();

        await SyncAsync(id, "Second", isCreate: false);

        DeletedFromTranslations().Should().BeTrue();
        (await _db.Queryable<ArticleTranslation>().Where(t => t.ArticleId == id).ToListAsync())
            .Should().ContainSingle().Which.Title.Should().Be("Second");
    }
}
