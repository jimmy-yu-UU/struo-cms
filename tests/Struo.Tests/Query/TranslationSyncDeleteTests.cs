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
/// on InnoDB). An update replaces the stored row of each supplied locale in place and inserts a row
/// for a new locale; it never deletes from the translation table, whose gap locks deadlock concurrent
/// updates of different items on InnoDB. Captures the SQL the repository executes, so the guard holds
/// on every backend the default suite runs on.
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

    private Task SyncAsync(Guid articleId, string locale, string title, string? body, bool isCreate)
    {
        var values = new Dictionary<string, object?> { ["title"] = title };
        if (body is not null) values["body"] = body;
        return _repo.SyncTranslationsAsync(
            typeof(ArticleTranslation), nameof(ArticleTranslation.ArticleId), nameof(ArticleTranslation.Locale),
            [nameof(ArticleTranslation.Title), nameof(ArticleTranslation.Body)], articleId,
            new Dictionary<string, IReadOnlyDictionary<string, object?>> { [locale] = values },
            isCreate);
    }

    private bool Deleted() =>
        _sql.Any(s => s.TrimStart().StartsWith("DELETE", StringComparison.OrdinalIgnoreCase));

    private Task<List<ArticleTranslation>> RowsAsync(Guid id) =>
        _db.Queryable<ArticleTranslation>().Where(t => t.ArticleId == id).ToListAsync();

    [Fact]
    public async Task Create_inserts_without_deleting_from_the_translation_table()
    {
        var id = Guid.CreateVersion7();

        await SyncAsync(id, "en", "First", null, isCreate: true);

        Deleted().Should().BeFalse();
        (await RowsAsync(id)).Should().ContainSingle().Which.Title.Should().Be("First");
    }

    [Fact]
    public async Task Update_of_an_existing_locale_replaces_the_row_in_place_without_deleting()
    {
        var id = Guid.CreateVersion7();
        await SyncAsync(id, "en", "First", "kept?", isCreate: true);
        var original = (await RowsAsync(id)).Single();
        _sql.Clear();

        await SyncAsync(id, "en", "Second", null, isCreate: false);

        Deleted().Should().BeFalse();
        var row = (await RowsAsync(id)).Should().ContainSingle().Which;
        row.Id.Should().Be(original.Id);
        row.Title.Should().Be("Second");
        row.Body.Should().BeNull("the update replaces the whole row, so a field it does not send is cleared");
    }

    [Fact]
    public async Task Update_adding_a_locale_inserts_it_and_leaves_the_other_locales_untouched()
    {
        var id = Guid.CreateVersion7();
        await SyncAsync(id, "en", "First", "body", isCreate: true);
        _sql.Clear();

        await SyncAsync(id, "zh-TW", "Zwei", null, isCreate: false);

        Deleted().Should().BeFalse();
        var rows = await RowsAsync(id);
        rows.Should().HaveCount(2);
        rows.Single(r => r.Locale == "en").Title.Should().Be("First");
        rows.Single(r => r.Locale == "zh-TW").Title.Should().Be("Zwei");
    }
}
