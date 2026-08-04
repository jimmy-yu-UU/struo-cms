using System.IO;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Query;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
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
public sealed class PostgresIntegrationTests : IDisposable
{
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
    // outlives them, and reusing a pooled physical connection across test boundaries is what made
    // exactly one test in this suite abort mid-read. See that class for the full diagnosis and why
    // this is isolation rather than tolerance.
    private static string? ResolveConnection()
    {
        var env = Environment.GetEnvironmentVariable(ConnEnv);
        if (!string.IsNullOrWhiteSpace(env)) return PgTestConnectionString.DisablePooling(env);

        var apiDir = FindApiDir();
        if (apiDir is null) return null;
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiDir, "appsettings.json"), optional: true)
            .AddJsonFile(Path.Combine(apiDir, "appsettings.Development.json"), optional: true)
            .Build();
        var conn = config["Testing:PostgresConnection"];
        return string.IsNullOrWhiteSpace(conn) ? null : PgTestConnectionString.DisablePooling(conn);
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

    // Same wiring as BuildRepo(), but also returns the RelationshipGraph/filter-resolver/options
    // needed to drive RelationExpander directly (self-relation N+1 check on real PG).
    private (IItemRepository Repo, RelationshipGraph Graph, IRelationFilterResolver FilterResolver, StruoQueryOptions Options)
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
        var filterResolver = new RelationFilterResolver(repo, graph, provider, registry, options);
        return (repo, graph, filterResolver, options);
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
        var (repo, graph, filterResolver, options) = BuildRepoWithGraph();

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
        var expander = new RelationExpander(counter, graph, filterResolver, options);
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

    // Query:MaxResolvedFilterIds on real Postgres. The cap bounds the intermediate id set a dotted
    // filter materializes before rewriting it into `id IN (...)`; uses category.parent (self-relation)
    // so only the Category table is needed. PG-specific risk being covered: the resolved set is a list
    // of Guids that has to bind as uuid, so a cap that fired on the wrong side of that binding — or a
    // rewrite that produced an untyped IN list — would pass on SQLite and fail here.
    [Fact]
    public async Task Resolved_id_set_cap_rejects_an_over_wide_dotted_filter_on_postgres()
    {
        if (!PgConfigured) return;
        var (repo, _, filterResolver, options) = BuildRepoWithGraph();

        const string stamp = "PgCapWide";
        for (var i = 0; i < 4; i++)
            await repo.CreateAsync("category", new Category { Name = $"{stamp}-p{i}" });

        options.MaxResolvedFilterIds = 3; // leaf resolves 4 parents -> over the cap

        var act = async () => await filterResolver.RewriteAsync(
            "category", new ComparisonFilter("parent.name", QueryOperator.Contains, stamp));

        (await act.Should().ThrowAsync<QueryException>())
            .WithMessage("*too many rows*")
            .WithMessage("*MaxResolvedFilterIds*");
    }

    // Control for the cap: a filter resolving WITHIN the cap must still rewrite to the correct
    // `id IN (...)` on Postgres, uuid-bound. Guards against the cap being enforced so eagerly that
    // legitimate dotted filters break, and against the rewrite losing the ids.
    [Fact]
    public async Task Resolved_id_set_cap_leaves_a_within_cap_dotted_filter_correct_on_postgres()
    {
        if (!PgConfigured) return;
        var (repo, _, filterResolver, options) = BuildRepoWithGraph();

        const string stamp = "PgCapNarrow";
        var parent = (Category)await repo.CreateAsync("category", new Category { Name = stamp });
        var childA = (Category)await repo.CreateAsync(
            "category", new Category { Name = $"{stamp}-a", ParentId = parent.Id });
        var childB = (Category)await repo.CreateAsync(
            "category", new Category { Name = $"{stamp}-b", ParentId = parent.Id });

        options.MaxResolvedFilterIds = 50;

        var rewritten = await filterResolver.RewriteAsync(
            "category", new ComparisonFilter("parent.name", QueryOperator.Eq, stamp));

        var comparison = rewritten.Should().BeOfType<ComparisonFilter>().Subject;
        comparison.FieldPath.Should().Be("id");
        comparison.Op.Should().Be(QueryOperator.In);
        comparison.Value.Should().BeAssignableTo<IReadOnlyList<object>>();
        ((IReadOnlyList<object>)comparison.Value!).Cast<Guid>()
            .Should().BeEquivalentTo([childA.Id, childB.Id]);
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
}
