// tests/Struo.Tests/Query/ItemServiceMaxLengthTests.cs
using System.Text.Json;
using AwesomeAssertions;
using Struo.Application.Configuration;
using Struo.Application.Query;
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

/// <summary>
/// Enforces <see cref="Struo.Domain.Metadata.Models.FieldMetadata.MaxLength"/> on both ItemService
/// write paths (7g.5): the non-translatable field loop in <c>Deserialize</c> and the per-locale
/// loop in <c>SyncTranslationsAsync</c>. Harness mirrors <see cref="ItemServiceRichTextSanitizationTests"/>.
/// </summary>
public class ItemServiceMaxLengthTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly ItemService _svc;

    public ItemServiceMaxLengthTests()
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
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        var types = new[] { typeof(Article), typeof(Category), typeof(Tag), typeof(Struo.Infrastructure.Files.File) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"] = typeof(Article), ["category"] = typeof(Category),
            ["tag"] = typeof(Tag), ["file"] = typeof(Struo.Infrastructure.Files.File),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, new StruoQueryOptions());
        var expander = new RelationExpander(repo, graph);
        var resolver = new RelationFilterResolver(repo, graph, provider, registry, new StruoQueryOptions());
        var languages = new LanguageProvider(db);
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, resolver, languages, new StruoQueryOptions(), new GanssHtmlSanitizer());
    }

    public void Dispose() => _file.Dispose();

    private static JsonElement JsonBody(object obj)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(obj));
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task Create_rejects_overlong_non_translatable_string_with_MaxLength_error()
    {
        var body = JsonBody(new { name = new string('x', 256) }); // category.name, effective 255
        var act = () => _svc.CreateAsync("category", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'name' exceeds maximum length 255.");
    }

    [Fact]
    public async Task Create_accepts_non_translatable_string_exactly_at_MaxLength()
    {
        var body = JsonBody(new { name = new string('x', 255) });
        var created = await _svc.CreateAsync("category", body); // must not throw
        created.Should().NotBeNull();
    }

    [Fact]
    public async Task Create_rejects_overlong_translatable_string_per_locale_MaxLength()
    {
        var body = JsonBody(new
        {
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = new string('x', 256), body = "<p>ok</p>" }
            }
        });
        var act = () => _svc.CreateAsync("article", body);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'title' exceeds maximum length 255 for locale 'en'.");
    }

    [Fact]
    public async Task Create_accepts_translatable_string_exactly_at_MaxLength()
    {
        var body = JsonBody(new
        {
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = new string('x', 255), body = "<p>ok</p>" }
            }
        });
        var created = await _svc.CreateAsync("article", body);
        created.Should().NotBeNull();
    }

    [Fact]
    public async Task Update_rejects_overlong_non_translatable_string_with_MaxLength_error()
    {
        var createBody = JsonBody(new { name = "Original" });
        var created = await _svc.CreateAsync("category", createBody);
        var id = created["id"]!.ToString()!;

        var updateBody = JsonBody(new { name = new string('x', 256) });
        var act = () => _svc.UpdateAsync("category", id, updateBody);
        await act.Should().ThrowAsync<QueryException>()
            .WithMessage("Field 'name' exceeds maximum length 255.");
    }
}
