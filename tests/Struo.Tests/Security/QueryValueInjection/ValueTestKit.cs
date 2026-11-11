// tests/Struo.Tests/Security/QueryValueInjection/ValueTestKit.cs
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Auditing;
using Struo.Domain.Query;
using Struo.Infrastructure.Persistence;
using Struo.Infrastructure.Query;
using Struo.Tests.Support;

namespace Struo.Tests.Security.QueryValueInjection;

public enum VcColor { Red = 0, Blue = 1, Green = 2 }

/// <summary>One row per supported scalar type; rows 1-3 carry distinct ascending values, row 4 is trashed.</summary>
[SugarTable("vc_typed")]
public sealed class VcTyped : ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    public int IntCol { get; set; }
    public long LongCol { get; set; }
    public short ShortCol { get; set; }
    public byte ByteCol { get; set; }
    public bool BoolCol { get; set; }
    public decimal DecimalCol { get; set; }
    public double DoubleCol { get; set; }
    public float FloatCol { get; set; }
    public Guid GuidCol { get; set; }
    public DateTime DateTimeCol { get; set; }
    public VcColor EnumCol { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? DeletedAt { get; set; }
    [SugarColumn(IsNullable = true)] public Guid? DeletedBy { get; set; }
}

/// <summary>String column with the awkward values SqlSugar treats specially.</summary>
[SugarTable("vc_strings")]
public sealed class VcString : ISoftDeletable
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? Code { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? Other { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? DeletedAt { get; set; }
    [SugarColumn(IsNullable = true)] public Guid? DeletedBy { get; set; }
}

/// <summary>Two columns whose names differ by a trailing number.</summary>
[SugarTable("vc_pairs")]
public sealed class VcPair
{
    [SugarColumn(IsPrimaryKey = true)] public long Id { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? Code { get; set; }
    [SugarColumn(Length = 64, IsNullable = true)] public string? Code1 { get; set; }
}

internal static class ValueTestKit
{
    public static readonly Guid[] Guids =
    [
        Guid.Parse("00000000-0000-0000-0000-0000000000a1"),
        Guid.Parse("00000000-0000-0000-0000-0000000000a2"),
        Guid.Parse("00000000-0000-0000-0000-0000000000a3"),
        Guid.Parse("00000000-0000-0000-0000-0000000000a4"),
    ];

    public static readonly DateTime Day = new(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc);

    public static (ITestDatabase File, ISqlSugarClient Db) Open()
    {
        var file = TestBackend.CreateUnitDatabase();
        return (file, SqlSugarClientFactory.Create(file.Options(), new TestCurrentUserAccessor(Guid.Empty)));
    }

    public static void Reset<T>(ISqlSugarClient db) where T : class, new()
    {
        var name = db.EntityMaintenance.GetTableName<T>();
        if (db.DbMaintenance.IsAnyTable(name, false)) db.DbMaintenance.DropTable(name);
        db.CodeFirst.InitTables<T>();
    }

    public static void SeedTyped(ISqlSugarClient db)
    {
        Reset<VcTyped>(db);
        var rows = Enumerable.Range(1, 4).Select(i => new VcTyped
        {
            Id = i,
            IntCol = i * 10, LongCol = i * 10_000_000_000L, ShortCol = (short)(i * 100), ByteCol = (byte)(i * 10),
            BoolCol = i % 2 == 1, DecimalCol = i * 1.5m, DoubleCol = i * 2.5, FloatCol = i * 0.5f,
            GuidCol = Guids[i - 1], DateTimeCol = Day.AddDays(i), EnumCol = (VcColor)(i % 3),
            DeletedAt = i == 4 ? Day : null,
        }).ToList();
        db.Insertable(rows).ExecuteCommand();
    }

    public static readonly (long Id, string? Code, string? Other, bool Trashed)[] StringRows =
    [
        (1, "abc", "o1", false), (2, "", null, false), (3, null, null, false), (4, "null", null, false),
        (5, "[null]", null, false), (6, "a,b", null, false), (7, "x\\", null, false), (8, "x'", "o1", false),
        (9, "zz", "o1", false), (10, "abc", "o1", true),
    ];

    public static void SeedStrings(ISqlSugarClient db)
    {
        Reset<VcString>(db);
        db.Insertable(StringRows.Select(s => new VcString
        {
            Id = s.Id, Code = s.Code, Other = s.Other, DeletedAt = s.Trashed ? Day : null
        }).ToList()).ExecuteCommand();
    }

    public static EntityDescriptor Descriptor<T>(params string[] props) =>
        new(typeof(T), props.ToDictionary(p => char.ToLowerInvariant(p[0]) + p[1..], p => p, StringComparer.OrdinalIgnoreCase), "Id");

    public static readonly string[] TypedProps =
        ["Id", "IntCol", "LongCol", "ShortCol", "ByteCol", "BoolCol", "DecimalCol", "DoubleCol", "FloatCol", "GuidCol", "DateTimeCol", "EnumCol"];

    public static List<long> Ids<T>(ISqlSugarClient db, List<IConditionalModel> conds, Func<T, long> id) where T : class, new() =>
        db.Queryable<T>().Where(conds).ToList().Select(id).OrderBy(x => x).ToList();

    public static (string Sql, List<SugarParameter> Pars) SqlOf<T>(ISqlSugarClient db, List<IConditionalModel> conds)
        where T : class, new()
    {
        var kv = db.Queryable<T>().Where(conds).ToSql();
        return (kv.Key, kv.Value);
    }

    public static List<IConditionalModel> Conds(
        ISqlSugarClient db, EntityDescriptor d, string field, QueryOperator op, object? value)
    {
        var list = new List<IConditionalModel>();
        if (ConditionalModelTranslator.ToCondition(new ComparisonFilter(field, op, value), d, db) is { } c) list.Add(c);
        return list;
    }
}

/// <summary>Owns a unit database for the lifetime of one test class instance.</summary>
internal sealed class UnitDb : IDisposable
{
    private readonly ITestDatabase _file;

    public UnitDb() => (_file, Db) = ValueTestKit.Open();

    public ISqlSugarClient Db { get; }

    public void Dispose() => _file.Dispose();
}
