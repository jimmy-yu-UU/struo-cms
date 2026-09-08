using System.Text.Json;
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Application.Search;
using Struo.Domain.Query;
using Struo.Infrastructure.Localization;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Revisions;
using Struo.Infrastructure.Security;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Search;

/// <summary>Spec §3.6 R1–R5 and R8 through the real ItemService on SQLite with a scripted provider.</summary>
public sealed class SearchProviderItemServiceTests : IDisposable
{
    private readonly SqliteTestDatabase _file = new();
    private readonly ISqlSugarClient _db;
    private readonly List<string> _sql = [];
    private readonly LanguageProvider _languages;
    private Func<SearchRequest, SearchOutcome> _script = _ => SearchOutcome.NotHandled;
    private readonly ScriptedSearchProvider _provider;
    private readonly ItemService _svc;
    private readonly StruoQueryOptions _options = new();
    private string _alpha, _beta, _gamma;

    public SearchProviderItemServiceTests()
    {
        var db = _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.Sqlite, ConnectionString = _file.ConnectionString },
            new TestCurrentUserAccessor(Guid.Empty));
        db.CodeFirst.InitTables<Article>();
        db.CodeFirst.InitTables<ArticleTranslation>();
        db.CodeFirst.InitTables<Language>();
        db.CodeFirst.InitTables<Tag>();
        db.CodeFirst.InitTables<ArticleTag>();
        db.CodeFirst.InitTables<Category>();
        db.CodeFirst.InitTables<Revision>();
        LanguageSeeder.SeedAsync(db).GetAwaiter().GetResult();

        Type[] types = [typeof(Article), typeof(Category), typeof(Tag), typeof(Struo.Infrastructure.Files.File), typeof(Struo.Infrastructure.Files.MediaFolder)];
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"] = typeof(Article), ["category"] = typeof(Category), ["tag"] = typeof(Tag),
            ["file"] = typeof(Struo.Infrastructure.Files.File), ["mediafolder"] = typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var repo = new SqlSugarItemRepository(db, registry, graph, provider, _options);
        var expander = new RelationExpander(repo, graph, _options);
        _languages = new LanguageProvider(db);
        var revisionUser = new TestCurrentUserAccessor(Guid.Empty);
        var revisionStore = new SqlSugarRevisionStore(db, revisionUser);
        var snapshotBuilder = new RevisionSnapshotBuilder(repo, provider, registry, graph);
        _provider = new ScriptedSearchProvider(r => _script(r));
        _svc = new ItemService(repo, provider, registry, new AllowAllPermissionService(),
            graph, expander, graph, _languages, _options, new GanssHtmlSanitizer(),
            revisionUser, revisionStore, snapshotBuilder, new NoopUserSessionRevocationService(), _provider);

        _alpha = Create("Alpha"); _beta = Create("Beta"); _gamma = Create("Gamma");
        db.Aop.OnLogExecuting = (sql, _) => _sql.Add(sql);
    }

    private string Create(string name)
    {
        using var body = JsonDocument.Parse($$"""{"name":"{{name}}"}""");
        return (string)_svc.CreateAsync("category", body.RootElement).GetAwaiter().GetResult()["id"]!.ToString()!;
    }

    // Dispose the SqlSugarClient's own connection before deleting the underlying SQLite file —
    // an undisposed connection can hold the file locked on Windows, making the file cleanup flaky.
    public void Dispose()
    {
        _db.Dispose();
        _file.Dispose();
    }

    private static QueryModel Q(string? search, FilterNode? filter = null) => new(null, filter, [], 25, 0, search);

    private static List<string> Names(PagedResult r) => r.Data.Select(d => (string)d["name"]!).ToList();

    [Fact]
    public async Task Blank_search_does_not_call_the_provider()
    {
        _script = _ => throw new InvalidOperationException("must not be called");
        await _svc.QueryAsync("category", Q(null));
        await _svc.QueryAsync("category", Q("   "));
        _provider.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Provider_receives_collection_term_effective_locale_and_searchable_fields()
    {
        await _svc.QueryAsync("category", Q("Alp"));
        _provider.Requests.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SearchRequest("category", "Alp", _languages.DefaultCode(), ["name"]),
                o => o.WithStrictOrdering());
        _provider.Requests.Clear();
        await _svc.QueryAsync("category", Q("Alp"), locale: "zh-TW");
        _provider.Requests.Single().Locale.Should().Be("zh-TW");
    }

    [Fact]
    public async Task Not_handled_falls_back_to_the_built_in_like_search()
    {
        var r = await _svc.QueryAsync("category", Q("Alp"));
        Names(r).Should().Equal("Alpha");
        r.Total.Should().Be(1);
        _sql.Should().Contain(s => s.Contains("LIKE", StringComparison.OrdinalIgnoreCase));
        _provider.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task Provider_receives_the_canonical_collection_name_even_for_a_differently_cased_request()
    {
        await _svc.QueryAsync("Category", Q("Alp"));
        _provider.Requests.Single().Collection.Should().Be("category");
    }

    [Fact]
    public async Task Candidates_restrict_rows_and_total_and_ignore_the_term()
    {
        _script = _ => SearchOutcome.Candidates([_beta, _gamma]);
        var r = await _svc.QueryAsync("category", new QueryModel(null, null, [new SortField("name", Descending: true)], 25, 0, "Alpha"));
        Names(r).Should().Equal("Gamma", "Beta");
        r.Total.Should().Be(2);
        _sql.Should().NotContain(s => s.Contains("LIKE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Candidates_are_anded_with_the_filter()
    {
        _script = _ => SearchOutcome.Candidates([_beta]);
        (await _svc.QueryAsync("category", Q("x", new ComparisonFilter("name", QueryOperator.Eq, "Alpha")))).Total.Should().Be(0);
        (await _svc.QueryAsync("category", Q("x", new ComparisonFilter("name", QueryOperator.Eq, "Beta")))).Total.Should().Be(1);
    }

    [Fact]
    public async Task Empty_candidates_yield_zero_rows_empty_facets_and_zero_count()
    {
        _script = _ => SearchOutcome.Candidates([]);
        var q = Q("x") with { Facets = ["name"], Aggregate = new AggregateSpec(new Dictionary<AggregateOp, IReadOnlyList<string>> { [AggregateOp.Count] = ["name"] }) };
        var r = await _svc.QueryAsync("category", q);
        r.Total.Should().Be(0);
        r.Data.Should().BeEmpty();
        r.Facets!.Single().Values.Should().BeEmpty();
        r.Aggregate!.Values[AggregateOp.Count]["name"].Should().Be(0L);
        _sql.Should().NotContain(s => s.Contains("LIKE", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Facets_and_aggregate_use_the_same_candidate_set_and_the_provider_is_asked_once()
    {
        _script = _ => SearchOutcome.Candidates([_alpha, _beta]);
        var q = Q("x") with { Facets = ["name"], Aggregate = new AggregateSpec(new Dictionary<AggregateOp, IReadOnlyList<string>> { [AggregateOp.Count] = ["name"] }) };
        var r = await _svc.QueryAsync("category", q);
        r.Total.Should().Be(2);
        r.Facets!.Single().Values.Select(b => ((string?)b.Value, b.Count)).Should().BeEquivalentTo([("Alpha", 1L), ("Beta", 1L)]);
        r.Aggregate!.Values[AggregateOp.Count]["name"].Should().Be(2L);
        _provider.Requests.Should().HaveCount(1);
    }

    [Fact]
    public async Task Soft_deleted_candidates_follow_the_deleted_mode_not_the_provider()
    {
        await _svc.DeleteAsync("category", _beta);
        _script = _ => SearchOutcome.Candidates([_alpha, _beta]);
        (await _svc.QueryAsync("category", Q("x"))).Total.Should().Be(1);
        (await _svc.QueryAsync("category", Q("x"), deleted: DeletedFilter.With)).Total.Should().Be(2);
        (await _svc.QueryAsync("category", Q("x"), deleted: DeletedFilter.Only)).Total.Should().Be(1);
    }

    [Fact]
    public async Task Get_never_calls_the_provider()
    {
        _script = _ => throw new InvalidOperationException("must not be called");
        (await _svc.GetAsync("category", _alpha)).Should().NotBeNull();
        _provider.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task Candidates_restrict_a_to_many_facets_buckets_to_the_candidate_rows()
    {
        // "articles" is category's O2M relation (Category.Articles, reverse of Article.CategoryId) —
        // a to-many facet path, unlike every other facet exercised in this file. One article under
        // Alpha, one under Beta: with no candidate restriction both show up; restricting the
        // candidate set to Alpha alone must drop Beta's article from the bucket count entirely, not
        // merely from the paginated category rows/total (which the sibling candidate tests above
        // already cover for an own-field facet) — this is the assertion that fails if a to-many
        // facet ever stopped honouring the candidate set.
        var articleInAlpha = await CreateArticle(_alpha);
        var articleInBeta = await CreateArticle(_beta);
        // "a" (lowercase, case-insensitive LIKE) matches every seeded category name (Alpha/Beta/
        // Gamma) so the unrestricted call's own LIKE fallback does not itself narrow the root rows —
        // the only thing that should narrow them below is the candidate restriction.
        var q = Q("a") with { Facets = ["articles"] };

        _script = _ => SearchOutcome.NotHandled;
        var unrestricted = await _svc.QueryAsync("category", q);
        unrestricted.Facets!.Single().Values.Select(b => (string?)b.Value)
            .Should().BeEquivalentTo([articleInAlpha, articleInBeta]);

        _script = _ => SearchOutcome.Candidates([_alpha]);
        var restricted = await _svc.QueryAsync("category", q);
        restricted.Facets!.Single().Values.Select(b => (string?)b.Value)
            .Should().Equal(articleInAlpha);
    }

    private async Task<string> CreateArticle(string categoryId)
    {
        // $$$$ (four dollars): the JSON ends in three consecutive closing braces
        // ("...{"title":"t"}}}"), which a 3-dollar raw interpolated string cannot disambiguate from
        // an interpolation-close (CS9007) — same fix as ItemServiceChangeNotificationTests.
        using var body = JsonDocument.Parse(
            $$$$"""{"status":"draft","categoryId":"{{{{categoryId}}}}","translations":{"en":{"title":"t"}}}""");
        var dict = await _svc.CreateAsync("article", body.RootElement);
        return dict["id"]!.ToString()!;
    }

    [Fact]
    public async Task Provider_contract_violations_surface_as_InvalidOperationException()
    {
        _options.MaxSearchCandidates = 1;
        _script = _ => SearchOutcome.Candidates([_alpha, _beta]);
        await FluentActions.Awaiting(() => _svc.QueryAsync("category", Q("x"))).Should().ThrowAsync<InvalidOperationException>();
        _options.MaxSearchCandidates = 1000;
        _script = _ => SearchOutcome.Candidates(["nope"]);
        await FluentActions.Awaiting(() => _svc.QueryAsync("category", Q("x"))).Should().ThrowAsync<InvalidOperationException>();
    }
}
