using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Application.Query.Write;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Infrastructure.Security;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// Opt-in PostgreSQL integration tests. These run against a REAL Postgres only when a
/// connection is configured — via the <c>STRUO_TEST_PG_CONNECTION</c> env var, or the Struo.Api
/// <c>Testing:PostgresConnection</c> appsettings key (Development overrides base); otherwise every
/// test is a no-op pass. The point is to catch the "SQLite-green ≠ Postgres-correct" class of bug
/// (uuid vs text casts, bigint, the optimistic-concurrency compare-and-swap) BEFORE it reaches a live deploy — the
/// project's live-gate discipline, automated as a suite you can run locally before merging.
///
/// Configure once in appsettings.Development.json (same place as the dev DB):
///   "Testing": { "PostgresConnection": "Host=localhost;Port=5432;Database=web-struo-cms-test-db;Username=postgres;Password=..." }
/// then: dotnet test --filter FullyQualifiedName~PostgresIntegrationTests
///
/// NOTE: this suite creates and CLEARS sample tables. It refuses to run unless the target database
/// name contains "test" — point it at a disposable database, never the dev/prod DB.
/// </summary>
[CollectionDefinition("Postgres", DisableParallelization = true)]
public sealed class PostgresCollectionDefinition { }

[Collection("Postgres")]
public sealed partial class PostgresIntegrationTests : IDisposable
{
    // Source-generated regexes (SYSLIB1045): the "sqN_" nesting-prefix SubQueryConditional
    // allocates per Wrap() call (see Two_nesting_levels_compose_through_Wrap_on_postgres below).
    [GeneratedRegex(@"sq(\d+)_")]
    private static partial Regex SqPrefixWithGroupRegex();

    [GeneratedRegex(@"sq\d+_")]
    private static partial Regex SqPrefixRegex();

    private const string ConnEnv = "STRUO_TEST_PG_CONNECTION";
    private static readonly string? Conn = ResolveConnection();
    private ISqlSugarClient? _db;

    // xunit 2.x has no runtime Assert.Skip; when PG isn't configured the tests early-return as a
    // trivial pass (a no-op). They only exercise Postgres when a connection is configured.
    private bool PgConfigured => !string.IsNullOrWhiteSpace(Conn);

    // Connection resolution (in order): the STRUO_TEST_PG_CONNECTION env var (CI / one-off), else the
    // Struo.Api appsettings key Testing:PostgresConnection (appsettings.Development.json overrides
    // appsettings.json) — so it's configured in the same place as the dev DB. Empty/absent -> skip.
    //
    // Whichever source wins, the resolved string goes through PgTestConnectionString.DisablePooling:
    // each test here builds and disposes its own client, but Npgsql's pool is process-wide and
    // outlives them, and reuse of a pooled physical connection across a connection-close boundary
    // (between tests, or between commands within one test) is what made one test in this suite abort
    // mid-read. See that class for the full diagnosis and why this is isolation rather than tolerance.
    //
    // The raw string is resolved FIRST and both sources share a SINGLE exit through DisablePooling.
    // That is deliberate: with one call site there is only one thing to drop instead of two, and
    // Resolved_connection_disables_pooling below covers the wiring for both sources at once rather
    // than only for whichever branch happened to run in a given process.
    private static string? ResolveConnection()
    {
        var raw = Environment.GetEnvironmentVariable(ConnEnv);

        if (string.IsNullOrWhiteSpace(raw))
        {
            var apiDir = FindApiDir();
            if (apiDir is null) return null;
            var config = new ConfigurationBuilder()
                .AddJsonFile(Path.Combine(apiDir, "appsettings.json"), optional: true)
                .AddJsonFile(Path.Combine(apiDir, "appsettings.Development.json"), optional: true)
                .Build();
            raw = config["Testing:PostgresConnection"];
        }

        return string.IsNullOrWhiteSpace(raw) ? null : PgTestConnectionString.DisablePooling(raw);
    }

    private static string? FindApiDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "src", "Struo.Api");
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }

    private IItemRepository BuildRepo() => BuildRepoWithGraph().Repo;

    // Same wiring as BuildRepo(), but also returns the RelationshipGraph/options needed to drive
    // RelationExpander directly (self-relation N+1 check on real PG).
    private (IItemRepository Repo, RelationshipGraph Graph, StruoQueryOptions Options)
        BuildRepoWithGraph()
    {
        GuardDisposableDatabase();

        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.PostgreSQL, ConnectionString = Conn! },
            new TestCurrentUserAccessor(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
        // Self-provision the disposable test DB when absent. Ignore failures (e.g. it already exists,
        // or the provider can't create it) — InitTables/queries below surface a real connection problem.
        try { _db.DbMaintenance.CreateDatabase(); } catch { /* already exists / not permitted */ }
        _db.CodeFirst.InitTables<Category>();
        _db.Deleteable<Category>().Where(x => true).ExecuteCommand(); // deterministic start

        var types = new[]
        {
            typeof(Article), typeof(Category), typeof(Tag), typeof(Struo.Infrastructure.Files.File),
            typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors([typeof(Article), typeof(Category), typeof(Tag)]));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"] = typeof(Article), ["category"] = typeof(Category),
            ["tag"] = typeof(Tag), ["file"] = typeof(Struo.Infrastructure.Files.File),
            ["mediafolder"] = typeof(Struo.Infrastructure.Files.MediaFolder),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        var options = new StruoQueryOptions();
        var repo = new SqlSugarItemRepository(_db, registry, graph, provider, options);
        return (repo, graph, options);
    }

    // Wires an ItemDeserializer against the SAME repo/graph BuildRepoWithGraph() just built, so the
    // create-binding allowlist tests below drive ItemDeserializer.Deserialize ->
    // SqlSugarItemRepository.CreateAsync — the exact pair ItemService.CreateAsync calls in
    // production — without pulling in the full ItemService (translations/M2M/revisions/permissions),
    // which this suite has no other use for and which ItemServiceCreateBindingTests (SQLite) already
    // covers at that layer. Chose a small dedicated helper over widening BuildRepoWithGraph()'s return
    // tuple: that tuple already has four call sites above, and every one of them would have to
    // destructure (and discard) a fifth member it doesn't need.
    private (IItemRepository Repo, ItemDeserializer Deserializer, IMetadataProvider Provider)
        BuildRepoWithDeserializer()
    {
        var (repo, graph, _) = BuildRepoWithGraph();
        var types = new[] { typeof(Article), typeof(Category), typeof(Tag) };
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var provider = new CachedMetadataProvider(MetadataScanner.ScanTypes(types));
        var deserializer = new ItemDeserializer(registry, graph, new RichTextCleaner(new GanssHtmlSanitizer()));
        return (repo, deserializer, provider);
    }

    // 安全守衛：這些測試會 DELETE 資料列、DROP 探針表。除非目標 DB 名稱含 "test" 一律拒跑，
    // 這樣一條誤指向 dev/prod 的連線永遠不可能清掉它。
    private void GuardDisposableDatabase()
    {
        var dbName = Conn!
            .Split(';')
            .Select(p => p.Trim())
            .FirstOrDefault(p => p.StartsWith("Database=", StringComparison.OrdinalIgnoreCase))
            ?.Split('=', 2)[1] ?? "";
        if (!dbName.Contains("test", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Refusing destructive PG tests against database '{dbName}': its name must contain 'test'. " +
                "Point Testing:PostgresConnection (or STRUO_TEST_PG_CONNECTION) at a disposable database, " +
                "e.g. Database=web-struo-cms-test-db.");
    }

    // schema 層級的探測不需要 repository/metadata wiring，只要一個連上可丟棄測試 DB 的 client。
    private ISqlSugarClient BuildRawClient()
    {
        GuardDisposableDatabase();
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.PostgreSQL, ConnectionString = Conn! },
            new TestCurrentUserAccessor(Guid.Empty));
        try { _db.DbMaintenance.CreateDatabase(); } catch { /* already exists / not permitted */ }
        return _db;
    }

    public void Dispose() => _db?.Dispose();

    // Guards the pooling fix against silent removal. PgTestConnectionString's own unit tests only
    // exercise DisablePooling in isolation, so without this a future edit could drop the call in
    // ResolveConnection and bring the abort documented in AGENTS.md back with nothing failing.
    // Asserts the wiring, not the helper's logic.
    //
    // Matched case- and whitespace-insensitively, matching DisablePooling's own IgnoreCase detection:
    // a maintainer who configures `pooling=false` or `Pooling = false` themselves has done exactly the
    // right thing, and a literal Contain("Pooling=false") would fail them with a message claiming the
    // fix was dropped.
    [Fact]
    public void Resolved_connection_disables_pooling()
    {
        if (!PgConfigured) return;
        Conn.Should().MatchRegex(
            new Regex(@"Pooling\s*=\s*false", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant),
            "ResolveConnection must route both of its sources through " +
            "PgTestConnectionString.DisablePooling. If you set Pooling yourself to re-investigate the " +
            "abort recorded in AGENTS.md, this test is the expected casualty of that choice; " +
            "otherwise the pooling fix has been dropped and the flake is back.");
    }

    // On real Postgres: non-page-aligned offset returns the exact window.
    [Fact]
    public async Task Offset_window_is_exact_on_postgres()
    {
        if (!PgConfigured) return;
        var repo = BuildRepo();
        for (var i = 0; i < 5; i++)
            await repo.CreateAsync("category", new Category { Name = $"C{i}" });

        var q = new QueryModel(null, null, [new SortField("name", false)], 2, 1, null);
        var result = await repo.QueryAsync("category", q, []);
        result.Total.Should().Be(5);
        result.Rows.Select(r => ((Category)r).Name).Should().Equal("C1", "C2");
    }

    // On real Postgres: compare-and-swap (WHERE id AND version=expected) rejects a stale update.
    [Fact]
    public async Task Stale_version_update_conflicts_on_postgres()
    {
        if (!PgConfigured) return;
        var repo = BuildRepo();
        var created = (Category)await repo.CreateAsync("category", new Category { Name = "A" });
        var id = created.Id.ToString();

        // First update: version 0 → 1.
        var reload1 = (Category)(await repo.GetByIdAsync("category", id))!;
        reload1.Name = "B"; reload1.Version = 0;
        await repo.UpdateAsync("category", id, reload1);

        // Second writer still holds version 0 → conflict.
        var stale = (Category)(await repo.GetByIdAsync("category", id))!;
        stale.Name = "C"; stale.Version = 0;
        var act = async () => await repo.UpdateAsync("category", id, stale);
        await act.Should().ThrowAsync<ConcurrencyConflictException>();
    }

    // uuid filter on real Postgres: filtering by the Guid PK must bind as uuid, not text (42883).
    [Fact]
    public async Task Uuid_id_filter_round_trips_on_postgres()
    {
        if (!PgConfigured) return;
        var repo = BuildRepo();
        var created = (Category)await repo.CreateAsync("category", new Category { Name = "Findme" });
        var rows = await repo.QueryWhereInAsync("category", "id", [created.Id]);
        rows.Cast<Category>().Select(c => c.Name).Should().Contain("Findme");
    }

    private static readonly System.Reflection.BindingFlags ReadPropFlags =
        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
        System.Reflection.BindingFlags.IgnoreCase;

    private static object? ReadProp(object e, string name) => e.GetType().GetProperty(name, ReadPropFlags)?.GetValue(e);

    // Non-SQLite leg: category.parent (self-relation M2O) 6-level ancestor chain on
    // REAL Postgres — mirrors DeepNestingBatchingTests' SQLite batching invariant (one WhereIn
    // query per level, linear in depth) but drives SqlSugarItemRepository against Postgres, where
    // the Guid FK (uuid column) binding is the PG-specific risk (same concern as
    // Uuid_id_filter_round_trips_on_postgres above). Also asserts the expansion resolves the
    // correct ancestor 6 hops up the chain, not just the query count.
    [Fact]
    public async Task Six_level_selfrelation_parent_chain_is_linear_and_correct_on_postgres()
    {
        if (!PgConfigured) return;
        var (repo, graph, options) = BuildRepoWithGraph();

        // root -> A1 -> A2 -> A3 -> A4 -> A5 -> leaf: exactly 6 `parent` hops from leaf to root.
        var root = (Category)await repo.CreateAsync("category", new Category { Name = "PgRoot" });
        var prevId = root.Id;
        for (var lvl = 1; lvl <= 5; lvl++)
        {
            var node = (Category)await repo.CreateAsync(
                "category", new Category { Name = $"PgA{lvl}", ParentId = prevId });
            prevId = node.Id;
        }
        var leaf = (Category)await repo.CreateAsync(
            "category", new Category { Name = "PgLeaf", ParentId = prevId });

        var counter = new CountingItemRepository(repo);
        var expander = new RelationExpander(counter, graph, options);
        var parents = await repo.QueryWhereInAsync("category", "id", new object[] { leaf.Id });

        DeepSpec? deep = null;
        for (var i = 0; i < 6; i++)
            deep = new DeepSpec(new Dictionary<string, DeepRelationSpec>
            {
                ["parent"] = new DeepRelationSpec(null, null, deep)
            });

        counter.ResetCount();
        var result = await expander.ExpandAsync(
            "category", parents, deep!,
            projectTarget: (_, entity, _) => new Dictionary<string, object?>
            {
                ["id"] = ReadProp(entity, "id"), ["name"] = ReadProp(entity, "name")
            },
            parentId: entity => ReadProp(entity, "id")!,
            readProp: ReadProp);

        // Linear in depth on real Postgres: exactly 1 WhereIn query per level (6 total), not
        // exponential and not one-query-per-entity.
        counter.WhereInCalls.Should().Be(6);

        var ancestor = result[leaf.Id];
        for (var i = 0; i < 6; i++)
            ancestor = (Dictionary<string, object?>)ancestor["parent"]!;
        ancestor["name"].Should().Be("PgRoot"); // 6 hops up the chain lands exactly on root
    }

    // U3 primitive probe on real Postgres: the wrapped IN (SELECT …) conditional at the top level of a
    // Where list, a typed one-column projection (quoted column, uuid), and an apostrophe literal.
    [Fact]
    public async Task Subquery_conditional_primitive_works_on_postgres()
    {
        if (!PgConfigured) return;
        var (repo, _, _) = BuildRepoWithGraph();
        _db!.CodeFirst.InitTables<Article>();

        var cat = (Category)await repo.CreateAsync("category", new Category { Name = "PgProbe O'Brien" });
        var other = (Category)await repo.CreateAsync("category", new Category { Name = "PgProbe other" });
        await repo.CreateAsync("article", new Article { Status = "draft", CategoryId = cat.Id });
        await repo.CreateAsync("article", new Article { Status = "draft", CategoryId = other.Id });

        var sel = (System.Linq.Expressions.Expression<Func<Category, Guid>>)ColumnSelectorFactory.TypedSelector(typeof(Category), "Id");
        var sub = _db.Queryable<Category>()
            .Where(new List<IConditionalModel> { new ConditionalModel { FieldName = "Name", ConditionalType = ConditionalType.Equal, FieldValue = "PgProbe O'Brien" } })
            .Select(sel).ToSql();
        var col = _db.EntityMaintenance.GetDbColumnName<Article>("CategoryId");
        var q = _db.Queryable<Article>().Where(new List<IConditionalModel> { SubQueryConditional.Wrap(SubQueryKind.In, col, sub) });

        q.ToSql().Key.Should().NotContain("N'");
        (await q.ToListAsync()).Should().ContainSingle().Which.CategoryId.Should().Be(cat.Id);
    }

    // U3 primitive probe: reproduces the exact collision the fix targets, on real Postgres. An outer
    // ConditionalModel and an inner (wrapped) subquery both filter Category.Name at conditional-list
    // index 0, in ONE plain top-level list — NOT inside a ConditionalCollections OR group, whose
    // members get a 1000-offset index and so never actually collide with a plain list's own naming.
    // The premise is enforced, not just commented: the outer leaf's own generated parameter name,
    // built standalone exactly as it will be built inside the real combined list below, is asserted
    // equal to the inner subquery's pre-rename parameter name. Categories only — no Article needed.
    [Fact]
    public async Task Subquery_conditional_parameters_do_not_collide_on_postgres()
    {
        if (!PgConfigured) return;
        BuildRepoWithGraph(); // establishes _db against the disposable test DB (Category only)

        var tech = new Category { Id = Guid.NewGuid(), Name = "PgCollide Tech" };
        var news = new Category { Id = Guid.NewGuid(), Name = "PgCollide News" };
        _db!.Insertable(new[] { tech, news }).ExecuteCommand();

        var idCol = _db.EntityMaintenance.GetDbColumnName<Category>("Id");
        var sel = (System.Linq.Expressions.Expression<Func<Category, Guid>>)ColumnSelectorFactory.TypedSelector(typeof(Category), "Id");

        KeyValuePair<string, List<SugarParameter>> CategoryIdsNamed(string name) =>
            _db.Queryable<Category>()
                .Where(new List<IConditionalModel> { new ConditionalModel { FieldName = "Name", ConditionalType = ConditionalType.Equal, FieldValue = name } })
                .Select(sel).ToSql();
        ConditionalModel OuterNameLeaf(string name) => new() { FieldName = "Name", ConditionalType = ConditionalType.Equal, FieldValue = name };

        // Enforce the premise: the outer leaf and the inner subquery's own filter are each index-0 of
        // their own plain top-level list, so SqlSugar assigns them the SAME generated name pre-rename.
        var outerStandalone = _db.Queryable<Category>().Where(new List<IConditionalModel> { OuterNameLeaf("PgCollide Tech") }).ToSql();
        var innerSubTech = CategoryIdsNamed("PgCollide Tech");
        outerStandalone.Value.Select(p => p.ParameterName).Should().BeEquivalentTo(
            innerSubTech.Value.Select(p => p.ParameterName),
            "the outer leaf and the inner subquery are each index-0 of their own plain top-level list, so SqlSugar assigns them the same generated name before renaming");

        // Positive: Category.Name = 'PgCollide Tech' AND Id IN (SELECT Id FROM categories WHERE Name = 'PgCollide Tech') -> Tech only.
        var condTech = SubQueryConditional.Wrap(SubQueryKind.In, idCol, innerSubTech);
        var qPositive = _db.Queryable<Category>().Where(new List<IConditionalModel> { OuterNameLeaf("PgCollide Tech"), condTech });
        qPositive.ToSql().Key.Should().NotContain("N'");
        (await qPositive.ToListAsync()).Should().ContainSingle().Which.Name.Should().Be("PgCollide Tech");

        // Negative: same outer leaf, but the inner subquery now filters 'PgCollide News'. If the outer
        // value had clobbered the inner one (the actual collision symptom), this would still spuriously
        // match Tech; it must instead return ZERO rows.
        var condNews = SubQueryConditional.Wrap(SubQueryKind.In, idCol, CategoryIdsNamed("PgCollide News"));
        var qNegative = _db.Queryable<Category>().Where(new List<IConditionalModel> { OuterNameLeaf("PgCollide Tech"), condNews });
        (await qNegative.ToListAsync()).Should().BeEmpty();
    }

    // Mirrors Two_nesting_levels_compose_through_Wrap on real Postgres, categories only: an outer
    // Category.Name = 'X' leaf sits in the SAME top-level list as a wrapped subquery whose own filter
    // is ALSO Category.Name = 'X' (self-referencing on Id), one level further wrapped again — proving
    // composition through Wrap works at nesting depth 2 with two distinct parameter prefixes, on PG.
    [Fact]
    public async Task Two_nesting_levels_compose_through_Wrap_on_postgres()
    {
        if (!PgConfigured) return;
        BuildRepoWithGraph(); // establishes _db against the disposable test DB (Category only)

        var x = new Category { Id = Guid.NewGuid(), Name = "PgNest X" };
        var other = new Category { Id = Guid.NewGuid(), Name = "PgNest Other" };
        _db!.Insertable(new[] { x, other }).ExecuteCommand();

        var idCol = _db.EntityMaintenance.GetDbColumnName<Category>("Id");
        var sel = (System.Linq.Expressions.Expression<Func<Category, Guid>>)ColumnSelectorFactory.TypedSelector(typeof(Category), "Id");

        KeyValuePair<string, List<SugarParameter>> CategoryIdsNamed(string name) =>
            _db.Queryable<Category>()
                .Where(new List<IConditionalModel> { new ConditionalModel { FieldName = "Name", ConditionalType = ConditionalType.Equal, FieldValue = name } })
                .Select(sel).ToSql();

        // Level 2 (innermost): SELECT id FROM categories WHERE name = 'PgNest X'
        var level2Wrapped = SubQueryConditional.Wrap(SubQueryKind.In, idCol, CategoryIdsNamed("PgNest X"));

        // Level 1 (middle): SELECT id FROM categories WHERE name = 'PgNest X' AND id IN (level 2)
        var level1 = _db.Queryable<Category>()
            .Where(new List<IConditionalModel>
            {
                new ConditionalModel { FieldName = "Name", ConditionalType = ConditionalType.Equal, FieldValue = "PgNest X" },
                level2Wrapped,
            })
            .Select(sel).ToSql();
        var level1Wrapped = SubQueryConditional.Wrap(SubQueryKind.In, idCol, level1);

        // Outer: Category.Name = 'PgNest X' AND Category.Id IN (level 1) — same top-level list.
        var q = _db.Queryable<Category>().Where(new List<IConditionalModel>
        {
            new ConditionalModel { FieldName = "Name", ConditionalType = ConditionalType.Equal, FieldValue = "PgNest X" },
            level1Wrapped,
        });
        var final = q.ToSql();

        final.Key.Should().NotContain("N'");
        var prefixes = SqPrefixWithGroupRegex().Matches(final.Key).Select(m => m.Groups[1].Value).Distinct().ToList();
        prefixes.Should().HaveCount(2, "two independent Wrap() calls (level2->level1, level1->outer) each allocate their own prefix");
        var doublyPrefixedName = final.Value.Select(p => p.ParameterName).Single(n => SqPrefixRegex().Count(n) == 2);
        final.Key.Should().Contain(doublyPrefixedName);

        (await q.ToListAsync()).Should().ContainSingle().Which.Id.Should().Be(x.Id);
    }

    // Task 4 relation-filter pushdown, on real Postgres: A1's two assertions (dotted each-exists vs
    // _some same-row bind), _none on O2M/M2O, _junction same-row bind, and apostrophe escaping — the
    // SqlSugarItemRepository-level scenarios SubqueryPushdownTests already covers on SQLite, replayed
    // against PostgreSQL (uuid FK typing, lowercase quoted identifiers, no N' national-string prefix).
    // Uses SubqueryPushdownHarness's fixture types directly, InitTables'd here and DropTable'd at the
    // end (GuardDisposableDatabase refuses a non-"test" database first).
    [Fact]
    public async Task Pushdown_some_none_and_junction_on_postgres()
    {
        if (!PgConfigured) return;
        GuardDisposableDatabase();
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.PostgreSQL, ConnectionString = Conn! },
            new TestCurrentUserAccessor(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
        try { _db.DbMaintenance.CreateDatabase(); } catch { /* already exists / not permitted */ }
        foreach (var t in SubqueryPushdownHarness.Types) _db.CodeFirst.InitTables(t);
        // Deterministic start (FK-safe order), in case a previous run was interrupted before DropTable.
        _db.Deleteable<SqProductLabel>().Where(x => true).ExecuteCommand();
        _db.Deleteable<SqProperty>().Where(x => true).ExecuteCommand();
        _db.Deleteable<SqProduct>().Where(x => true).ExecuteCommand();
        _db.Deleteable<SqLabel>().Where(x => true).ExecuteCommand();
        _db.Deleteable<SqCategory>().Where(x => true).ExecuteCommand();

        try
        {
            var collections = MetadataScanner.ScanTypes(SubqueryPushdownHarness.Types);
            var metadata = new CachedMetadataProvider(collections);
            var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(SubqueryPushdownHarness.Types));
            var graph = new RelationshipGraph(collections, new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
            {
                ["sqCategory"] = typeof(SqCategory), ["sqProduct"] = typeof(SqProduct), ["sqProperty"] = typeof(SqProperty),
                ["sqLabel"] = typeof(SqLabel), ["sqProductLabel"] = typeof(SqProductLabel),
                ["sqSoftLabel"] = typeof(SqSoftLabel), ["sqProductSoftLabel"] = typeof(SqProductSoftLabel),
            });
            var options = new StruoQueryOptions();
            var repo = new SqlSugarItemRepository(_db, registry, graph, metadata, options);
            var translator = new FilterTranslator(_db, graph, metadata, registry, options);

            var tech = new SqCategory { Id = Guid.NewGuid(), Name = "PgTech" };
            var archive = new SqCategory { Id = Guid.NewGuid(), Name = "PgArchive" };
            _db.Insertable(new[] { tech, archive }).ExecuteCommand();

            var cross = new SqProduct { Id = Guid.NewGuid(), Name = "pgcross", CategoryId = tech.Id };
            var same = new SqProduct { Id = Guid.NewGuid(), Name = "pgsame", CategoryId = archive.Id };
            var noProps = new SqProduct { Id = Guid.NewGuid(), Name = "pgbare", CategoryId = tech.Id };
            var noCat = new SqProduct { Id = Guid.NewGuid(), Name = "pgnocat", CategoryId = null };
            _db.Insertable(new[] { cross, same, noProps, noCat }).ExecuteCommand();
            _db.Insertable(new[]
            {
                new SqProperty { Id = Guid.NewGuid(), ProductId = cross.Id, Code = "vds-v", ValueNum = 20 },
                new SqProperty { Id = Guid.NewGuid(), ProductId = cross.Id, Code = "ptot-w", ValueNum = 100 },
                new SqProperty { Id = Guid.NewGuid(), ProductId = same.Id, Code = "vds-v", ValueNum = 80 },
                new SqProperty { Id = Guid.NewGuid(), ProductId = noCat.Id, Code = "o'neil", ValueNum = 1 },
            }).ExecuteCommand();
            var guide = new SqLabel { Id = Guid.NewGuid(), Name = "PgGuide" };
            var misc = new SqLabel { Id = Guid.NewGuid(), Name = "PgMisc" };
            _db.Insertable(new[] { guide, misc }).ExecuteCommand();
            _db.Insertable(new[]
            {
                new SqProductLabel { Id = Guid.NewGuid(), ProductId = cross.Id, LabelId = guide.Id, Note = "hero" },
                new SqProductLabel { Id = Guid.NewGuid(), ProductId = same.Id, LabelId = guide.Id, Note = "plain" },
                new SqProductLabel { Id = Guid.NewGuid(), ProductId = same.Id, LabelId = misc.Id, Note = "hero" },
            }).ExecuteCommand();

            async Task<List<Guid>> IdsAsync(FilterNode filter)
            {
                var q = new QueryModel(null, filter, [], 100, 0, null);
                var r = await repo.QueryAsync("sqProduct", q, ["name"]);
                return r.Rows.Cast<SqProduct>().Select(p => p.Id).ToList();
            }

            // A1a: dotted path — each-exists, matches across two different rows' own properties.
            (await IdsAsync(new LogicalFilter(LogicalOperator.And,
                [
                    new ComparisonFilter("properties.code", QueryOperator.Eq, "vds-v"),
                    new ComparisonFilter("properties.valueNum", QueryOperator.Gte, 60),
                ])))
                .Should().BeEquivalentTo([cross.Id, same.Id]);

            // A1b: _some — binds both conditions to the SAME related row.
            (await IdsAsync(new RelationPredicateFilter("properties", RelationQuantifier.Some,
                new LogicalFilter(LogicalOperator.And,
                [
                    new ComparisonFilter("code", QueryOperator.Eq, "vds-v"),
                    new ComparisonFilter("valueNum", QueryOperator.Gte, 60),
                ]))))
                .Should().BeEquivalentTo([same.Id]);

            // _none on O2M: parents with no related rows are included.
            (await IdsAsync(new RelationPredicateFilter(
                "properties", RelationQuantifier.None, new ComparisonFilter("code", QueryOperator.Eq, "vds-v"))))
                .Should().BeEquivalentTo([noProps.Id, noCat.Id]);

            // _none on M2O: null foreign keys are included.
            (await IdsAsync(new RelationPredicateFilter(
                "category", RelationQuantifier.None, new ComparisonFilter("name", QueryOperator.Eq, "PgTech"))))
                .Should().BeEquivalentTo([same.Id, noCat.Id]);

            // _junction: target and junction condition bound to the same link.
            (await IdsAsync(new RelationPredicateFilter("labels", RelationQuantifier.Some,
                new LogicalFilter(LogicalOperator.And,
                [
                    new ComparisonFilter("name", QueryOperator.Eq, "PgGuide"),
                    new ComparisonFilter("_junction.note", QueryOperator.Eq, "hero"),
                ]))))
                .Should().BeEquivalentTo([cross.Id]);

            // Apostrophe in the inner value is escaped, not a syntax error.
            (await IdsAsync(new RelationPredicateFilter(
                "properties", RelationQuantifier.Some, new ComparisonFilter("code", QueryOperator.Eq, "o'neil"))))
                .Should().BeEquivalentTo([noCat.Id]);

            // _or over two relation conditions composes (fix round 1: merged into ONE conditional so no
            // ConditionalCollections ever holds two adjacent SqlSugar ICustomConditionalFunc entries).
            (await IdsAsync(new LogicalFilter(LogicalOperator.Or,
                [
                    new RelationPredicateFilter("labels", RelationQuantifier.Some, new ComparisonFilter("name", QueryOperator.Eq, "PgGuide")),
                    new ComparisonFilter("category.name", QueryOperator.Eq, "PgArchive"),
                ])))
                .Should().BeEquivalentTo([cross.Id, same.Id]);
            (await IdsAsync(new LogicalFilter(LogicalOperator.Or,
                [
                    new RelationPredicateFilter("labels", RelationQuantifier.None, new ComparisonFilter("name", QueryOperator.Eq, "PgGuide")),
                    new ComparisonFilter("category.name", QueryOperator.Eq, "PgArchive"),
                ])))
                .Should().BeEquivalentTo([same.Id, noProps.Id, noCat.Id]);

            // _or whose children are ALL subqueries (no scalar sibling) — the ConditionalCollections
            // ends up with exactly one merged entry.
            (await IdsAsync(new LogicalFilter(LogicalOperator.Or,
                [
                    new ComparisonFilter("category.name", QueryOperator.Eq, "PgTech"),
                    new ComparisonFilter("labels.name", QueryOperator.Eq, "PgMisc"),
                ])))
                .Should().BeEquivalentTo([cross.Id, noProps.Id, same.Id]);

            // No SQL Server-style N'...' national-string prefix anywhere in the generated SQL.
            var noneConds = translator.Translate("sqProduct",
                new RelationPredicateFilter("properties", RelationQuantifier.None, new ComparisonFilter("code", QueryOperator.Eq, "x")),
                null, null, [], null);
            _db.Queryable<SqProduct>().Where(noneConds).ToSql().Key.Should().NotContain("N'");

            var m2mConds = translator.Translate(
                "sqProduct", new ComparisonFilter("labels.name", QueryOperator.Eq, "PgGuide"), null, null, [], null);
            _db.Queryable<SqProduct>().Where(m2mConds).ToSql().Key.Should().NotContain("N'");

            var orConds = translator.Translate("sqProduct", new LogicalFilter(LogicalOperator.Or,
                [
                    new ComparisonFilter("category.name", QueryOperator.Eq, "PgTech"),
                    new ComparisonFilter("labels.name", QueryOperator.Eq, "PgMisc"),
                ]), null, null, [], null);
            _db.Queryable<SqProduct>().Where(orConds).ToSql().Key.Should().NotContain("N'");
        }
        finally
        {
            foreach (var name in new[] { "sq_product_labels", "sq_properties", "sq_products", "sq_labels", "sq_categories" })
                try { if (_db.DbMaintenance.IsAnyTable(name, false)) _db.DbMaintenance.DropTable(name); }
                catch { /* best-effort cleanup */ }
        }
    }

    // Task 8, live PG: a two-hop self-relation dotted filter (category.parent.name) with a
    // deliberately wide sibling set at the walk-back hop — the scenario the retired resolved-id-set
    // cap used to bound as a materialized, cardinality-checked id set — is answered as a correlated
    // SQL subquery (IN (SELECT …)) on real Postgres, and querying with those conditionals returns
    // exactly the matching children.
    [Fact]
    public async Task Wide_dotted_filter_is_answered_in_sql_on_postgres()
    {
        if (!PgConfigured) return;
        var (repo, graph, options) = BuildRepoWithGraph();

        var stamp = "PgCapWide" + Guid.NewGuid().ToString("N")[..8];
        var parent = (Category)await repo.CreateAsync("category", new Category { Name = $"PgCapWideParent-{stamp}" });
        var children = new List<Category>();
        for (var i = 0; i < 4; i++)
            children.Add((Category)await repo.CreateAsync(
                "category", new Category { Name = $"PgCapWide-p{i}-{stamp}", ParentId = parent.Id }));

        var types = new[] { typeof(Article), typeof(Category), typeof(Tag) };
        var collections = MetadataScanner.ScanTypes(types);
        var metadata = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var translator = new FilterTranslator(_db!, graph, metadata, registry, options);

        var conds = translator.Translate(
            "category", new ComparisonFilter("parent.name", QueryOperator.Contains, stamp), null, null, [], null);
        var sql = _db!.Queryable<Category>().Where(conds).ToSql();
        sql.Key.Should().Contain("IN (SELECT");
        sql.Key.Should().NotContain("N'");

        var rows = await _db.Queryable<Category>().Where(conds).ToListAsync();
        rows.Select(c => c.Id).Should().BeEquivalentTo(children.Select(c => c.Id));
    }

    // Task 6, live PG: the translation-sidecar subquery's own shape — article_translations has a
    // `long` identity PK (Id) and projects a `uuid` FK (ArticleId) that the outer article query
    // compares its own uuid `id` column against. This is the SQLite-green/Postgres-risky combination
    // AGENTS.md calls out for FK typing (see Uuid_id_filter_round_trips_on_postgres above): a
    // long-keyed sidecar table projecting a uuid column into an outer IN (SELECT …) must still bind
    // that column as uuid, not text, on real Postgres.
    [Fact]
    public async Task Translatable_leaf_subquery_binds_uuid_fk_on_postgres()
    {
        if (!PgConfigured) return;
        var (repo, _, _) = BuildRepoWithGraph();
        _db!.CodeFirst.InitTables<Article>();
        _db.CodeFirst.InitTables<ArticleTranslation>();
        _db.Deleteable<ArticleTranslation>().Where(x => true).ExecuteCommand();
        _db.Deleteable<Article>().Where(x => true).ExecuteCommand();

        var stamp = "PgTr" + Guid.NewGuid().ToString("N")[..6];
        var hit = (Article)await repo.CreateAsync("article", new Article { Status = "draft" });
        var miss = (Article)await repo.CreateAsync("article", new Article { Status = "draft" });
        _db.Insertable(new ArticleTranslation { ArticleId = hit.Id, Locale = "en", Title = stamp + "-en" }).ExecuteCommand();
        _db.Insertable(new ArticleTranslation { ArticleId = miss.Id, Locale = "en", Title = "other-" + stamp }).ExecuteCommand();

        var types = new[] { typeof(Article), typeof(Category), typeof(Tag) };
        var collections = MetadataScanner.ScanTypes(types);
        var metadata = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var graph = new RelationshipGraph(collections, new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"] = typeof(Article), ["category"] = typeof(Category), ["tag"] = typeof(Tag),
        });
        var translator = new FilterTranslator(_db, graph, metadata, registry, new StruoQueryOptions());

        var conds = translator.Translate(
            "article", new ComparisonFilter("title", QueryOperator.Eq, stamp + "-en"), null, null, [], "en");
        var sql = _db.Queryable<Article>().Where(conds).ToSql();
        sql.Key.Should().Contain("article_translations");
        sql.Key.Should().NotContain("N'");

        var rows = await _db.Queryable<Article>().Where(conds).ToListAsync();
        rows.Should().ContainSingle().Which.Id.Should().Be(hit.Id);
    }

    // The create-binding allowlist (bf04229, ItemDeserializer.Deserialize) on real Postgres. Once a
    // client-supplied "id" is stripped from the body, the entity reaches
    // SqlSugarItemRepository.CreateAsync with Id == Guid.Empty — exactly the condition that makes it
    // mint a Guid.CreateVersion7() id (SqlSugarItemRepository.CreateAsync). Doing this TWICE with the SAME
    // client-supplied id and asserting two DIFFERENT minted ids is the point: a single create can't
    // tell "minted a fresh id" apart from "silently inserted the all-zero uuid", since either way the
    // row's id would just come back as something other than the client's Guid.Empty-adjacent value
    // read alone. If minting silently failed to run on Postgres, the first create would insert the
    // literal zero uuid and the second would collide on the primary key and throw, never reaching the
    // final assertion below.
    [Fact]
    public async Task Create_binding_allowlist_mints_distinct_ids_for_a_repeated_client_supplied_id_on_postgres()
    {
        if (!PgConfigured) return;
        var (repo, deserializer, provider) = BuildRepoWithDeserializer();
        var meta = provider.GetCollection("category")!;
        var clientId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var body = JsonDocument.Parse($$"""{"id":"{{clientId}}","name":"PgAllowlistId"}""").RootElement;

        var first = (Category)await repo.CreateAsync("category", deserializer.Deserialize("category", body, meta));
        var second = (Category)await repo.CreateAsync("category", deserializer.Deserialize("category", body, meta));

        first.Id.Should().NotBe(clientId);
        second.Id.Should().NotBe(clientId);
        second.Id.Should().NotBe(first.Id,
            "two mints off the same stripped Guid.Empty must be distinct; if minting silently failed, " +
            "the all-zero uuid would insert once and the second create would throw a PK collision " +
            "instead of reaching this assertion");
    }

    // Companion to the id-minting test above: a declared ManyToOne FK (Category.ParentId, a uuid
    // column on Postgres) must still bind through the create allowlist and persist. Guid/uuid FK
    // binding is this suite's own catalogued PG-specific risk class — see
    // Uuid_id_filter_round_trips_on_postgres and
    // Six_level_selfrelation_parent_chain_is_linear_and_correct_on_postgres above — and the same risk
    // ItemServiceCreateBindingTests.Create_still_sets_a_declared_many_to_one_foreign_key already flags
    // (on SQLite) as the reason that regression guard exists.
    [Fact]
    public async Task Create_binding_allowlist_still_persists_a_declared_manytoone_fk_on_postgres()
    {
        if (!PgConfigured) return;
        var (repo, deserializer, provider) = BuildRepoWithDeserializer();
        var meta = provider.GetCollection("category")!;

        var parentBody = JsonDocument.Parse("""{"name":"PgAllowlistFkParent"}""").RootElement;
        var parent = (Category)await repo.CreateAsync(
            "category", deserializer.Deserialize("category", parentBody, meta));

        var childBody = JsonDocument.Parse(
            "{\"name\":\"PgAllowlistFkChild\",\"parentId\":\"" + parent.Id + "\"}").RootElement;
        var child = (Category)await repo.CreateAsync(
            "category", deserializer.Deserialize("category", childBody, meta));

        var reloaded = (Category)(await repo.GetByIdAsync("category", child.Id.ToString()))!;
        reloaded.ParentId.Should().Be(parent.Id);
    }

    // The credential-write audit path on real Postgres. SqlSugarUserAccountStore's mutators are
    // column-scoped SetColumns updates that chain UpdatedAt/UpdatedBy/Version + 1, and two of the
    // values written are NULL (a system actor's UpdatedBy, and AccessToken on revoke). An untyped null
    // parameter is sent to Npgsql as text, which PG rejects against uuid/varchar columns (42804) —
    // exactly the divergence SQLite hides, since it ignores the distinction. This asserts all three
    // mutators round-trip on PG, including the null cases.
    [Fact]
    public async Task User_credential_writes_stamp_audit_and_bump_version_on_postgres()
    {
        if (!PgConfigured) return;
        BuildRepoWithGraph();                       // establishes _db against the disposable test DB
        _db!.CodeFirst.InitTables<Struo.Infrastructure.Identity.User>();

        var store = new Struo.Infrastructure.Identity.SqlSugarUserAccountStore(_db);
        var actor = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
        var email = $"pg-cred-{Guid.CreateVersion7():N}@struo.test";

        var id = await store.CreateAsync(email, "enc:initial", "PG Cred Target");
        (await store.ExistsAsync(id)).Should().BeTrue();
        (await store.FindProfileAsync(id))!.Email.Should().Be(email);

        Struo.Infrastructure.Identity.User Load() =>
            _db!.Queryable<Struo.Infrastructure.Identity.User>().Where(u => u.Id == id).First();

        var created = Load();

        (await store.SetPasswordAsync(id, "enc:rotated", actor, DateTime.UtcNow)).Should().BeTrue();
        var afterPassword = Load();
        afterPassword.Password.Should().Be("enc:rotated");
        afterPassword.Version.Should().Be(created.Version + 1);
        afterPassword.UpdatedBy.Should().Be(actor);

        (await store.SetAccessTokenAsync(id, "token-hash", actor, DateTime.UtcNow)).Should().BeTrue();
        var afterIssue = Load();
        afterIssue.AccessToken.Should().Be("token-hash");
        afterIssue.AccessTokenCreatedAt.Should().NotBeNull();
        afterIssue.AccessTokenLastUsedAt.Should().BeNull("SetAccessTokenAsync writes a typed NULL here");
        afterIssue.Version.Should().Be(afterPassword.Version + 1);

        // Both null-valued columns in one statement: AccessToken (varchar) and UpdatedBy (uuid).
        (await store.ClearAccessTokenAsync(id, actor: null, DateTime.UtcNow)).Should().BeTrue();
        var afterRevoke = Load();
        afterRevoke.AccessToken.Should().BeNull();
        afterRevoke.UpdatedBy.Should().BeNull("a system actor writes a typed NULL uuid, not PG error 42804");
        afterRevoke.Version.Should().Be(afterIssue.Version + 1);

        // Unknown id reports false rather than throwing, so the controllers' 404 stays correct.
        (await store.SetPasswordAsync(Guid.CreateVersion7(), "enc:x", actor, DateTime.UtcNow))
            .Should().BeFalse();

        _db.Deleteable<Struo.Infrastructure.Identity.User>().Where(u => u.Id == id).ExecuteCommand();
    }

    // 未過濾的 InitTables 在真 Postgres 上對既有表做什麼。SQLite 不驗證宣告型別、其 dialect 的
    // 結構同步能力也與 PG 不同，所以「InitTables 會 DROP COLUMN」這個架構前提只有在這裡才證得出來。
    // 實測（2026-08-03，SqlSugarCore 5.1.4.215）：在 PostgreSQL 上，Doomed 欄位被 DROP 掉了——
    // 與 SQLite 的量測結果（DatabaseInitializerTests）相反。
    [Fact]
    public void Unfiltered_InitTables_drops_a_removed_column_on_postgres()
    {
        if (!PgConfigured) return;
        var db = BuildRawClient();
        try
        {
            if (db.DbMaintenance.IsAnyTable("destructive_init_probe", false))
                db.DbMaintenance.DropTable("destructive_init_probe");

            db.CodeFirst.InitTables(typeof(DestructiveInitProbeWide));
            db.DbMaintenance.GetColumnInfosByTableName("destructive_init_probe", false)
              .Select(c => c.DbColumnName.ToLowerInvariant())
              .Should().Contain("doomed", "前置條件：探針表必須先帶有這一欄");

            db.CodeFirst.InitTables(typeof(DestructiveInitProbeNarrow));

            var columnsAfter = db.DbMaintenance.GetColumnInfosByTableName("destructive_init_probe", false)
              .Select(c => c.DbColumnName.ToLowerInvariant())
              .ToList();
            columnsAfter.Should().Contain("id", "同一次 rebuild 不應連帶丟失其他欄位");
            columnsAfter.Should().Contain("keep", "同一次 rebuild 不應連帶丟失其他欄位");
            columnsAfter.Should().NotContain("doomed",
                "entity 移除屬性後，未過濾的 InitTables 在 PostgreSQL 上 DROP COLUMN");
        }
        finally
        {
            try
            {
                if (db.DbMaintenance.IsAnyTable("destructive_init_probe", false))
                    db.DbMaintenance.DropTable("destructive_init_probe");
            }
            catch { /* best-effort cleanup; don't mask the real failure */ }
        }
    }

    // A5: 只標 IsJson、無 [CmsField]/[ColumnShape]/ColumnDataType 的欄位，在真 PG 上必須是 text 而非 varchar(1)。
    [Fact]
    public void Bare_IsJson_column_is_text_on_postgres_and_round_trips()
    {
        if (!PgConfigured) return;
        var db = BuildRawClient();
        try
        {
            if (db.DbMaintenance.IsAnyTable("ddl_closeout_isjson_probe", false))
                db.DbMaintenance.DropTable("ddl_closeout_isjson_probe");
            db.CodeFirst.InitTables<DdlCloseoutIsJsonProbe>();

            var dataType = db.Ado.GetString(
                "SELECT data_type FROM information_schema.columns WHERE table_name = 'ddl_closeout_isjson_probe' AND column_name = 'tags'");
            dataType.Should().Be("text");

            db.Insertable(new DdlCloseoutIsJsonProbe { Tags = ["alpha", "beta", "gamma"] }).ExecuteCommand();
            db.Queryable<DdlCloseoutIsJsonProbe>().First()!.Tags.Should().Equal("alpha", "beta", "gamma");
        }
        finally
        {
            try { if (db.DbMaintenance.IsAnyTable("ddl_closeout_isjson_probe", false)) db.DbMaintenance.DropTable("ddl_closeout_isjson_probe"); }
            catch { /* best-effort cleanup */ }
        }
    }

    // A6: FileTranslation 不再手寫 UniqueGroupNameList；帶 policy 的 client 建表後，PG 必須有覆蓋 (fileid, locale) 的 UNIQUE。
    [Fact]
    public void Sidecar_unique_index_is_derived_on_postgres_without_hand_written_attributes()
    {
        if (!PgConfigured) return;
        GuardDisposableDatabase();
        var policy = TranslationSidecarIndexPolicy.FromMetadata(
            MetadataScanner.ScanTypes([typeof(Struo.Infrastructure.Files.File), typeof(Struo.Infrastructure.Files.MediaFolder)]));
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.PostgreSQL, ConnectionString = Conn! },
            new TestCurrentUserAccessor(Guid.Empty), policy);
        var db = _db;
        try
        {
            if (db.DbMaintenance.IsAnyTable("file_translations", false))
                db.DbMaintenance.DropTable("file_translations");
            db.CodeFirst.InitTables<Struo.Infrastructure.Files.FileTranslation>();

            var indexDefs = db.Ado.SqlQuery<string>("SELECT indexdef FROM pg_indexes WHERE tablename = 'file_translations'");
            indexDefs.Should().Contain(d =>
                d.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase)
                && d.Contains("fileid", StringComparison.OrdinalIgnoreCase)
                && d.Contains("locale", StringComparison.OrdinalIgnoreCase));

            var fileId = Guid.NewGuid();
            db.Insertable(new Struo.Infrastructure.Files.FileTranslation { FileId = fileId, Locale = "en", Title = "a" }).ExecuteCommand();
            var dup = () => db.Insertable(new Struo.Infrastructure.Files.FileTranslation { FileId = fileId, Locale = "en", Title = "b" }).ExecuteCommand();
            dup.Should().Throw<Exception>().Which.Message.Should().Contain("23505");
        }
        finally
        {
            try { db.Deleteable<Struo.Infrastructure.Files.FileTranslation>().Where(x => true).ExecuteCommand(); }
            catch { /* best-effort cleanup */ }
        }
    }

    // Task 2's diff-and-patch M2M sync (SyncManyToManyAsync / ManyToManySync) on real Postgres.
    // SQLite (ManyToManySyncTests) never distinguishes a typed NULL from an untyped one, and never
    // exercises a real uuid PK/FK column — both are PG-specific risks this asserts against rows READ
    // BACK from the database at every step: PK constancy across a payload write and a reorder,
    // Alias round-tripping through its renamed column, a typed NULL succeeding (the 42804 trap this
    // whole suite exists to catch), and the duplicate-row repair keeping the lowest PK without
    // throwing.
    [Fact]
    public async Task Junction_payload_sync_preserves_keys_and_patches_columns_on_postgres()
    {
        if (!PgConfigured) return;
        var db = BuildRawClient();
        try
        {
            if (db.DbMaintenance.IsAnyTable("junction_sync_probe", false))
                db.DbMaintenance.DropTable("junction_sync_probe");
            db.CodeFirst.InitTables<JunctionSyncProbe>();

            var collections = MetadataScanner.ScanTypes([]);
            var repo = new SqlSugarItemRepository(
                db,
                new EntityRegistry(MetadataScanner.ScanDescriptors([])),
                new RelationshipGraph(collections, new Dictionary<string, Type>()),
                new CachedMetadataProvider(collections),
                new StruoQueryOptions());

            var parentId = Guid.NewGuid();
            var c1 = Guid.NewGuid();
            var c2 = Guid.NewGuid();
            var c3 = Guid.NewGuid();

            Task Sync(params JunctionLink[] links) => repo.SyncManyToManyAsync(
                typeof(JunctionSyncProbe), nameof(JunctionSyncProbe.ParentId), nameof(JunctionSyncProbe.ChildId),
                nameof(JunctionSyncProbe.Sort), parentId, links);

            List<JunctionSyncProbe> Rows() => db.Queryable<JunctionSyncProbe>()
                .Where(l => l.ParentId == parentId).OrderBy(l => l.Sort).ToList();

            // Step 1: bare ids -> 2 rows, sorts 0,1; record the minted PKs.
            await Sync(JunctionLink.Bare(c1), JunctionLink.Bare(c2));
            var s1 = Rows();
            s1.Select(r => r.ChildId).Should().Equal(c1, c2);
            s1.Select(r => r.Sort).Should().Equal(0, 1);
            var c1Pk = s1.Single(r => r.ChildId == c1).Id;
            var c2Pk = s1.Single(r => r.ChildId == c2).Id;

            // Step 2: payload on c1 (Note + the renamed Alias column) -> PKs unchanged.
            await Sync(
                new JunctionLink(c1, new Dictionary<string, object?> { ["Note"] = "n", ["Alias"] = "a" }),
                JunctionLink.Bare(c2));
            var s2 = Rows();
            s2.Single(r => r.ChildId == c1).Id.Should().Be(c1Pk, "a payload write must not recreate the row");
            s2.Single(r => r.ChildId == c2).Id.Should().Be(c2Pk);
            var c1Row2 = s2.Single(r => r.ChildId == c1);
            c1Row2.Note.Should().Be("n");
            c1Row2.Alias.Should().Be("a", "the payload key is the CLR name Alias but must reach the note_text column on Postgres");

            // Step 3: reorder (c2, c1) -> PKs unchanged; sorts swap; c1's payload survives untouched.
            await Sync(JunctionLink.Bare(c2), JunctionLink.Bare(c1));
            var s3 = Rows();
            s3.Select(r => r.ChildId).Should().Equal(c2, c1);
            s3.Select(r => r.Sort).Should().Equal(0, 1);
            s3.Single(r => r.ChildId == c1).Id.Should().Be(c1Pk, "a reorder must not recreate the row");
            s3.Single(r => r.ChildId == c2).Id.Should().Be(c2Pk);
            var c1Row3 = s3.Single(r => r.ChildId == c1);
            c1Row3.Note.Should().Be("n");
            c1Row3.Alias.Should().Be("a");

            // Step 4: c1 gets Weight only (Note untouched, merge semantics); c3 is inserted at sort 2.
            await Sync(
                new JunctionLink(c1, new Dictionary<string, object?> { ["Weight"] = 5 }),
                JunctionLink.Bare(c2),
                JunctionLink.Bare(c3));
            var s4 = Rows();
            s4.Select(r => r.ChildId).Should().Equal(c1, c2, c3);
            s4.Select(r => r.Sort).Should().Equal(0, 1, 2);
            var c1Row4 = s4.Single(r => r.ChildId == c1);
            c1Row4.Id.Should().Be(c1Pk);
            c1Row4.Weight.Should().Be(5);
            c1Row4.Note.Should().Be("n", "a Weight-only payload must merge, not overwrite Note");
            var c3Row4 = s4.Single(r => r.ChildId == c3);
            c3Row4.Sort.Should().Be(2);

            // Step 5: c1's Note explicitly set to null (typed-NULL 42804 trap); Weight kept; c3 removed.
            await Sync(
                new JunctionLink(c1, new Dictionary<string, object?> { ["Note"] = null }),
                JunctionLink.Bare(c2));
            var s5 = Rows();
            s5.Select(r => r.ChildId).Should().Equal(c1, c2);
            var c1Row5 = s5.Single(r => r.ChildId == c1);
            c1Row5.Id.Should().Be(c1Pk);
            c1Row5.Note.Should().BeNull("an explicit null payload value must reach Postgres as a typed NULL, not text 'null' (42804)");
            c1Row5.Weight.Should().Be(5, "Note=null must not disturb the previously-set Weight");

            // Step 6: a hand-inserted duplicate (parent, c2) row is repaired down to one, keeping
            // whichever PK Postgres itself orders lowest — matching ManyToManySync's own
            // `ORDER BY {pkColumn} ASC` — without throwing.
            var beforeDup = Rows().Single(r => r.ChildId == c2);
            var duplicateId = Guid.NewGuid();
            db.Insertable(new JunctionSyncProbe { Id = duplicateId, ParentId = parentId, ChildId = c2, Sort = 99 })
                .ExecuteCommand();
            var expectedSurvivorId = db.Queryable<JunctionSyncProbe>()
                .Where(r => r.Id == beforeDup.Id || r.Id == duplicateId)
                .OrderBy(r => r.Id)
                .Select(r => r.Id)
                .First();

            await Sync(JunctionLink.Bare(c1), JunctionLink.Bare(c2));

            var s6 = Rows();
            s6.Select(r => r.ChildId).Should().Equal(c1, c2);
            s6.Where(r => r.ChildId == c2).Should().ContainSingle(
                "the duplicate must be repaired down to exactly one row").Which.Id.Should().Be(expectedSurvivorId);
        }
        finally
        {
            try
            {
                if (db.DbMaintenance.IsAnyTable("junction_sync_probe", false))
                    db.DbMaintenance.DropTable("junction_sync_probe");
            }
            catch { /* best-effort cleanup; don't mask the real failure */ }
        }
    }
}
