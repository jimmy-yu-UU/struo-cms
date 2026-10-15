// tests/Struo.Tests/Query/TypedOrderBySecurityTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public sealed class TypedOrderBySecurityTests : IDisposable
{
    private static readonly Guid Tester = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();
    private readonly ISqlSugarClient _db;
    private readonly SqlSugarItemRepository _repo;
    private readonly List<(string Sql, List<SugarParameter> Parameters)> _log = [];

    private readonly Guid _a1 = Guid.CreateVersion7();
    private readonly Guid _a2 = Guid.CreateVersion7();
    private readonly Guid _a3 = Guid.CreateVersion7();
    private readonly Guid _a4 = Guid.CreateVersion7();

    public TypedOrderBySecurityTests()
    {
        _db = SqlSugarClientFactory.Create(_file.Options(), new TestCurrentUserAccessor(Tester));
        _db.CodeFirst.InitTables<Category>();
        _db.CodeFirst.InitTables<Article>();
        _db.CodeFirst.InitTables<ArticleTranslation>();
        var types = new[] { typeof(Article), typeof(Category), typeof(Tag) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var graph = new RelationshipGraph(collections, new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"] = typeof(Article), ["category"] = typeof(Category), ["tag"] = typeof(Tag),
        });
        _repo = new SqlSugarItemRepository(_db, registry, graph, provider, new StruoQueryOptions());
        Seed();
        _db.Aop.OnLogExecuting = (sql, pars) => _log.Add((sql, (pars ?? []).ToList()));
    }

    public void Dispose() => _file.Dispose();

    private void Seed()
    {
        var pz = new Category { Id = Guid.CreateVersion7(), Name = "Zeta" };
        var pa = new Category { Id = Guid.CreateVersion7(), Name = "Alpha" };
        var c1 = new Category { Id = Guid.CreateVersion7(), Name = "c1", ParentId = pz.Id };
        var c2 = new Category { Id = Guid.CreateVersion7(), Name = "c2", ParentId = pa.Id };
        var c3 = new Category { Id = Guid.CreateVersion7(), Name = "c3" };
        _db.Insertable(new List<Category> { pz, pa, c1, c2, c3 }).ExecuteCommand();
        _db.Insertable(new List<Article>
        {
            new() { Id = _a1, CategoryId = c1.Id, Status = "published" },
            new() { Id = _a2, CategoryId = c2.Id, Status = "published" },
            new() { Id = _a3, CategoryId = null, Status = "draft" },
            new() { Id = _a4, CategoryId = c3.Id, Status = "published" },
        }).ExecuteCommand();
        _db.Insertable(new List<ArticleTranslation>
        {
            new() { ArticleId = _a1, Locale = "en", Title = "Charlie" },
            new() { ArticleId = _a2, Locale = "en", Title = "Alpha" },
            new() { ArticleId = _a4, Locale = "en", Title = "Bravo" },
        }).ExecuteCommand();
    }

    private static QueryModel Sorted(params string[] fields) =>
        new(null, null, fields.Select(f => f.StartsWith('-') ? new SortField(f[1..], true) : new SortField(f, false)).ToList(), 50, 0, null);

    private async Task<List<Guid>> Ids(QueryModel q, string? locale = null) =>
        (await _repo.QueryAsync("article", q, [], locale)).Rows.Select(r => ((Article)r).Id).ToList();

    [Fact]
    public async Task Translatable_sort_binds_locale_as_parameter()
    {
        _log.Clear();
        await Ids(Sorted("title"), "en");

        var select = _log.Where(l => l.Sql.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase)).ToList();
        select.Should().NotBeEmpty();
        select.Should().OnlyContain(l => !l.Sql.Contains("'en'"));
        select.Should().Contain(l => l.Parameters.Any(p => Equals(p.Value, "en")));
    }

    [Theory]
    [InlineData("x\\' OR 1=1 #")]
    [InlineData("x' OR '1'='1")]
    [InlineData("'")]
    public async Task Hostile_locale_is_inert(string hostile)
    {
        _db.Insertable(new ArticleTranslation { ArticleId = _a3, Locale = hostile, Title = "Evil" }).ExecuteCommand();

        var desc = await Ids(Sorted("-title"), hostile);
        var asc = await Ids(Sorted("title"), hostile);

        desc.Should().HaveCount(4);
        asc.Should().HaveCount(4);
        // a3 holds the only non-NULL key, so it sits at opposite ends of the two directions.
        var ends = new[] { (desc[0], asc[^1]), (desc[^1], asc[0]) };
        ends.Should().Contain(e => e.Item1 == _a3 && e.Item2 == _a3);
    }

    [Fact]
    public async Task Relation_path_sort_has_no_sql_literal_assembly()
    {
        // category.parent.name: a2 -> Alpha, a1 -> Zeta, a4 (parentless category) and a3 (no category) -> NULL.
        var ids = await Ids(Sorted("category.parent.name"));

        ids.Should().HaveCount(4);
        ids.Where(i => i == _a1 || i == _a2).Should().Equal(_a2, _a1);
        var nulls = ids.Where(i => i == _a3 || i == _a4).ToList();
        nulls.Should().BeEquivalentTo([_a3, _a4]);
        // NULLs form one contiguous block, tied rows are in PK order.
        var firstNull = ids.IndexOf(nulls[0]);
        ids.Skip(firstNull).Take(2).Should().BeEquivalentTo(nulls);
        var dbOrder = _db.Queryable<Article>().Where(a => a.Id == _a3 || a.Id == _a4).OrderBy(a => a.Id).Select(a => a.Id).ToList();
        nulls.Should().Equal(dbOrder);
    }

    [Fact]
    public async Task Explicit_id_sort_adds_no_second_tiebreak()
    {
        _log.Clear();
        await Ids(Sorted("-id"));

        var idCol = _db.EntityMaintenance.GetDbColumnName<Article>(nameof(Article.Id));
        var select = _log.Single(l => l.Sql.Contains("ORDER BY", StringComparison.OrdinalIgnoreCase) && l.Sql.Contains("articles"));
        var orderBy = select.Sql[select.Sql.LastIndexOf("ORDER BY", StringComparison.OrdinalIgnoreCase)..];
        System.Text.RegularExpressions.Regex.Matches(orderBy, $@"\b{idCol}\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Count.Should().Be(1);
    }

    [Fact]
    public async Task Sort_field_case_is_insensitive()
    {
        var lower = await Ids(Sorted("title"), "en");
        var upper = await Ids(Sorted("Title"), "en");

        upper.Should().Equal(lower);
        lower.Where(i => i != _a3).Should().Equal(_a2, _a4, _a1);
    }
}
