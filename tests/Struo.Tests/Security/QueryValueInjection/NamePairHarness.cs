// tests/Struo.Tests/Security/QueryValueInjection/NamePairHarness.cs
using Microsoft.Extensions.Logging.Abstractions;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Tests.Support;

namespace Struo.Tests.Security.QueryValueInjection;

/// <summary>A collection with two text columns whose names differ by a trailing number.</summary>
[SugarTable("qv_name_pairs")]
[CmsCollection("Qv name pair")]
public sealed class QvNamePair : AuditableEntity, ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }
    public DateTime? DeletedAt { get; set; }
    public Guid? DeletedBy { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)]
    [CmsField(Label = "Code", Interface = FieldInterface.Text)] public string? Code { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)]
    [CmsField(Label = "Code1", Interface = FieldInterface.Text)] public string? Code1 { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)]
    [CmsField(Label = "Other", Interface = FieldInterface.Text)] public string? Other { get; set; }
}

internal sealed class NamePairHarness : IDisposable
{
    private readonly ITestDatabase _database = TestBackend.CreateUnitDatabase();

    public NamePairHarness()
    {
        Db = SqlSugarClientFactory.Create(_database.Options(), new TestCurrentUserAccessor(Guid.Empty));
        Db.CodeFirst.InitTables(typeof(QvNamePair));
        Type[] types = [typeof(QvNamePair)];
        var collections = MetadataScanner.ScanTypes(types);
        var graph = new RelationshipGraph(collections, new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            ["qvNamePair"] = typeof(QvNamePair),
        });
        Repo = new SqlSugarItemRepository(
            Db, new EntityRegistry(MetadataScanner.ScanDescriptors(types)), graph,
            new CachedMetadataProvider(collections), new StruoQueryOptions(), NullLogger<SqlSugarItemRepository>.Instance);
    }

    public ISqlSugarClient Db { get; }

    public SqlSugarItemRepository Repo { get; }

    public void Seed(params (string? Code, string? Code1, string? Other)[] rows) =>
        Db.Insertable(rows.Select(r => new QvNamePair { Id = Guid.NewGuid(), Code = r.Code, Code1 = r.Code1, Other = r.Other }).ToList())
            .ExecuteCommand();

    public async Task<List<(string? Code, string? Code1)>> QueryAsync(Struo.Domain.Query.FilterNode? filter)
    {
        var r = await Repo.QueryAsync("qvNamePair", new Struo.Domain.Query.QueryModel(null, filter, [], 100, 0, null), [], null);
        return r.Rows.Cast<QvNamePair>().Select(p => (p.Code, p.Code1)).ToList();
    }

    public async Task<List<(string? Code, string? Code1)>> SearchAsync(
        string search, string[] searchableFields, Struo.Domain.Query.FilterNode? filter = null)
    {
        var r = await Repo.QueryAsync(
            "qvNamePair", new Struo.Domain.Query.QueryModel(null, filter, [], 100, 0, search), searchableFields, null);
        return r.Rows.Cast<QvNamePair>().Select(p => (p.Code, p.Code1)).ToList();
    }

    public void Dispose() => _database.Dispose();
}
