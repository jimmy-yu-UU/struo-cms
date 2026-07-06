using AwesomeAssertions;
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
/// Opt-in PostgreSQL integration tests (audit D4). These run against a REAL Postgres only when the
/// <c>STRUO_TEST_PG_CONNECTION</c> environment variable is set (e.g. a throwaway local database);
/// otherwise every test is skipped. The point is to catch the "SQLite-green ≠ Postgres-correct" class
/// of bug (uuid vs text casts, bigint, the D2 compare-and-swap) BEFORE it reaches a live deploy —
/// the project's live-gate discipline, automated as a suite you can run locally before merging.
///
/// Run locally:
///   STRUO_TEST_PG_CONNECTION="Host=localhost;Port=5432;Database=struo_test;Username=postgres;Password=postgres" \
///     dotnet test --filter FullyQualifiedName~PostgresIntegrationTests
///
/// NOTE: this suite creates and clears the sample tables in the target database — point it at a
/// disposable database, never production.
/// </summary>
public sealed class PostgresIntegrationTests : IDisposable
{
    private const string ConnEnv = "STRUO_TEST_PG_CONNECTION";
    private readonly string? _conn = Environment.GetEnvironmentVariable(ConnEnv);
    private ISqlSugarClient? _db;

    // xunit 2.x has no runtime Assert.Skip; when PG isn't configured the tests early-return as a
    // trivial pass (a no-op). They only exercise Postgres when the env var is set.
    private bool PgConfigured => !string.IsNullOrWhiteSpace(_conn);

    private IItemRepository BuildRepo()
    {
        _db = SqlSugarClientFactory.Create(
            new DatabaseOptions { DbType = StruoDbType.PostgreSQL, ConnectionString = _conn! },
            new TestCurrentUserAccessor(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa")));
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
