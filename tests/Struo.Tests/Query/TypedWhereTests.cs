// tests/Struo.Tests/Query/TypedWhereTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

public sealed class TypedWhereTests : IDisposable
{
    private static readonly Guid Tester = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");

    // The FK ("order") and a plain column ("group") map to reserved words: the FK is quoted in the purge WHERE,
    // both are quoted in the update SET list.
    [SugarTable("typed_where_reserved")]
    [CmsCollection("Reserved")]
    public sealed class Reserved : AuditableEntity, ISoftDeletable
    {
        [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
        public DateTime? DeletedAt { get; set; }
        public Guid? DeletedBy { get; set; }

        [SugarColumn(ColumnName = "order", IsNullable = true)]
        public Guid? Order { get; set; }

        [SugarColumn(ColumnName = "group")]
        public string Group { get; set; } = string.Empty;
    }

    private readonly ITestDatabase _file = TestBackend.CreateUnitDatabase();
    private readonly ISqlSugarClient _db;
    private readonly SqlSugarItemRepository _repo;
    private readonly List<(string Sql, List<SugarParameter> Parameters)> _log = [];

    public TypedWhereTests()
    {
        _db = SqlSugarClientFactory.Create(_file.Options(), new TestCurrentUserAccessor(Tester));
        _db.CodeFirst.InitTables<FxCategory>();
        _db.CodeFirst.InitTables<FxArticle>();
        _db.CodeFirst.InitTables<Reserved>();
        var types = new[] { typeof(FxArticle), typeof(FxCategory), typeof(FxTag), typeof(Reserved) };
        var collections = MetadataScanner.ScanTypes(types);
        var provider = new CachedMetadataProvider(collections);
        var registry = new EntityRegistry(MetadataScanner.ScanDescriptors(types));
        var graph = new RelationshipGraph(collections, new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["fxArticle"] = typeof(FxArticle), ["fxCategory"] = typeof(FxCategory), ["fxTag"] = typeof(FxTag), ["reserved"] = typeof(Reserved),
        });
        _repo = new SqlSugarItemRepository(_db, registry, graph, provider, new StruoQueryOptions());
        _db.Aop.OnLogExecuting = (sql, pars) => _log.Add((sql, (pars ?? []).ToList()));
    }

    public void Dispose() => _file.Dispose();

    private Guid SeedArticle(Guid? categoryId = null)
    {
        var id = Guid.CreateVersion7();
        _db.Insertable(new FxArticle { Id = id, CategoryId = categoryId, Status = "draft" }).ExecuteCommand();
        return id;
    }

    private Guid SeedReserved(Guid? order = null)
    {
        var id = Guid.CreateVersion7();
        _db.Insertable(new Reserved { Id = id, Order = order, Group = "g" }).ExecuteCommand();
        return id;
    }

    private List<(string Sql, List<SugarParameter> Parameters)> Updates() =>
        _log.Where(l => l.Sql.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase)).ToList();

    private static void AssertBound(List<(string Sql, List<SugarParameter> Parameters)> updates, params object[] values)
    {
        updates.Should().ContainSingle();
        var (sql, pars) = updates[0];
        pars.Should().NotContain(p => p.ParameterName.Contains("__"), "no hand-named parameter remains");
        foreach (var v in values)
        {
            pars.Should().Contain(p => p.Value != null && p.Value.ToString() == v.ToString(), $"{v} is a bound parameter");
            sql.Should().NotContain($"'{v}'", "values are never inlined");
        }
    }

    [Fact]
    public async Task Optimistic_update_binds_id_and_version_as_parameters()
    {
        var id = SeedArticle();
        _log.Clear();

        await _repo.UpdateAsync("fxArticle", id.ToString(), new FxArticle { Status = "published", Version = 0 });

        AssertBound(Updates(), id, 0L);
    }

    [Fact]
    public async Task Stale_version_update_conflicts()
    {
        var id = SeedArticle();
        await _repo.UpdateAsync("fxArticle", id.ToString(), new FxArticle { Status = "published", Version = 0 });

        var act = () => _repo.UpdateAsync("fxArticle", id.ToString(), new FxArticle { Status = "draft", Version = 0 });

        await act.Should().ThrowAsync<ConcurrencyConflictException>();
        var row = (FxArticle)(await _repo.GetByIdAsync("fxArticle", id.ToString()))!;
        row.Status.Should().Be("published");
        row.Version.Should().Be(1);
    }

    [Fact]
    public async Task Trash_and_restore_bind_the_id_and_are_idempotent()
    {
        var id = SeedArticle();
        _log.Clear();

        (await _repo.SoftDeleteAsync("fxArticle", id.ToString(), DateTime.UtcNow, null)).Should().BeTrue();
        AssertBound(Updates(), id);
        (await _repo.SoftDeleteAsync("fxArticle", id.ToString(), DateTime.UtcNow, null)).Should().BeFalse();

        _log.Clear();
        (await _repo.RestoreAsync("fxArticle", id.ToString())).Should().BeTrue();
        AssertBound(Updates(), id);
        (await _repo.RestoreAsync("fxArticle", id.ToString())).Should().BeFalse();
    }

    [Fact]
    public async Task Purge_nulls_the_referencing_foreign_key_including_trashed_rows()
    {
        var categoryId = Guid.CreateVersion7();
        _db.Insertable(new FxCategory { Id = categoryId, Name = "c" }).ExecuteCommand();
        var live = SeedArticle(categoryId);
        var trashed = SeedArticle(categoryId);
        var other = SeedArticle(null);
        await _repo.SoftDeleteAsync("fxArticle", trashed.ToString(), DateTime.UtcNow, null);
        _log.Clear();

        await _repo.SetForeignKeyNullAsync("fxArticle", "categoryId", categoryId);

        AssertBound(Updates(), categoryId);
        foreach (var id in new[] { live, trashed, other })
        {
            var row = (FxArticle)(await _repo.GetByIdAsync("fxArticle", id.ToString(), DeletedFilter.With))!;
            row.CategoryId.Should().BeNull();
        }
    }

    [Fact]
    public async Task Reserved_word_columns_work_for_update_trash_restore_and_the_purge_filter()
    {
        var target = Guid.CreateVersion7();
        var id = SeedReserved(target);

        var updated = (Reserved)(await _repo.UpdateAsync("reserved", id.ToString(),
            new Reserved { Order = target, Group = "changed", Version = 0 }))!;
        updated.Group.Should().Be("changed");

        (await _repo.SoftDeleteAsync("reserved", id.ToString(), DateTime.UtcNow, null)).Should().BeTrue();
        (await _repo.SoftDeleteAsync("reserved", id.ToString(), DateTime.UtcNow, null)).Should().BeFalse();
        (await _repo.RestoreAsync("reserved", id.ToString())).Should().BeTrue();

        await _repo.SetForeignKeyNullAsync("reserved", "order", target);
        var row = (Reserved)(await _repo.GetByIdAsync("reserved", id.ToString()))!;
        row.Order.Should().BeNull();
    }
}
