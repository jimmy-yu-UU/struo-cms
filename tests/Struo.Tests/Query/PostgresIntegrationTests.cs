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
/// Opt-in PostgreSQL integration tests (audit D4). These run against a REAL Postgres only when a
/// connection is configured — via the <c>STRUO_TEST_PG_CONNECTION</c> env var, or the Struo.Api
/// <c>Testing:PostgresConnection</c> appsettings key (Development overrides base); otherwise every
/// test is a no-op pass. The point is to catch the "SQLite-green ≠ Postgres-correct" class of bug
/// (uuid vs text casts, bigint, the D2 compare-and-swap) BEFORE it reaches a live deploy — the
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
    private static string? ResolveConnection()
    {
        var env = Environment.GetEnvironmentVariable(ConnEnv);
        if (!string.IsNullOrWhiteSpace(env)) return env;

        var apiDir = FindApiDir();
        if (apiDir is null) return null;
        var config = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(apiDir, "appsettings.json"), optional: true)
            .AddJsonFile(Path.Combine(apiDir, "appsettings.Development.json"), optional: true)
            .Build();
        var conn = config["Testing:PostgresConnection"];
        return string.IsNullOrWhiteSpace(conn) ? null : conn;
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

    private IItemRepository BuildRepo()
    {
        // Safety guard: these tests DELETE rows. Refuse to run unless the target database name contains
        // "test", so a connection accidentally pointed at the dev/prod DB can never wipe it.
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

        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.PostgreSQL, ConnectionString = Conn! },
            new TestCurrentUserAccessor(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
        // Self-provision the disposable test DB when absent. Ignore failures (e.g. it already exists,
        // or the provider can't create it) — InitTables/queries below surface a real connection problem.
        try { _db.DbMaintenance.CreateDatabase(); } catch { /* already exists / not permitted */ }
        _db.CodeFirst.InitTables<Category>();
        _db.Deleteable<Category>().Where(x => true).ExecuteCommand(); // deterministic start

        var types = new[] { typeof(Article), typeof(Category), typeof(Tag), typeof(Struo.Infrastructure.Files.File) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors([typeof(Article), typeof(Category), typeof(Tag)]));
        var collectionTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["article"] = typeof(Article), ["category"] = typeof(Category),
            ["tag"] = typeof(Tag), ["file"] = typeof(Struo.Infrastructure.Files.File),
        };
        var graph = new RelationshipGraph(collections, collectionTypes);
        return new SqlSugarItemRepository(_db, registry, graph, provider, new StruoQueryOptions());
    }

    public void Dispose() => _db?.Dispose();

    // D5 on real Postgres: non-page-aligned offset returns the exact window.
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

    // D2 on real Postgres: compare-and-swap (WHERE id AND version=expected) rejects a stale update.
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
}
