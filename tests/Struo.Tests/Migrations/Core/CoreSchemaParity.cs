using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Migrations.Schema;
using Struo.Infrastructure.Persistence;

namespace Struo.Tests.Migrations.Core;

/// <summary>
/// Compares the framework tables one prefix holds (built by CodeFirst) with those another prefix holds
/// (built by the core migrations): columns, nullability, reported type and length, primary key and the
/// unique and non-unique index column sets. Index names are not compared.
/// </summary>
internal static class CoreSchemaParity
{
    /// <summary>A cosmetic difference a backend reports between FluentMigrator and SqlSugar DDL for identical storage.</summary>
    internal sealed record Allowance(
        string Name, StruoDbType Db, string Aspect, Func<DbColumnInfo, DbColumnInfo, bool> Matches, string Reason);

    private const string SqliteAffinity =
        "SQLite stores values by column affinity, not by the declared type name or length; both names share one affinity.";

    internal static readonly IReadOnlyList<Allowance> Allowed =
    [
        new("sqlite varchar vs text", StruoDbType.Sqlite, "type",
            (cf, mg) => Norm(cf.DataType) == "varchar" && Norm(mg.DataType) == "text", SqliteAffinity),
        new("sqlite varchar(n) length vs unsized text", StruoDbType.Sqlite, "length",
            (cf, mg) => Norm(cf.DataType) == "varchar" && Norm(mg.DataType) == "text" && cf.Length > 0 && mg.Length == 0,
            SqliteAffinity),
        new("sqlite bigint vs integer", StruoDbType.Sqlite, "type",
            (cf, mg) => Norm(cf.DataType) == "bigint" && Norm(mg.DataType) == "integer", SqliteAffinity),
        new("sqlite bit vs integer", StruoDbType.Sqlite, "type",
            (cf, mg) => Norm(cf.DataType) == "bit" && Norm(mg.DataType) == "integer", SqliteAffinity),
        new("sqlserver varchar vs nvarchar", StruoDbType.SqlServer, "type",
            (cf, mg) => Norm(cf.DataType) == "varchar" && Norm(mg.DataType) == "nvarchar" && cf.Length == mg.Length,
            "Same length; nvarchar is a strict superset of varchar, so every value CodeFirst can store the migration column also stores."),
        new("mysql varchar(36) vs char(36)", StruoDbType.MySql, "type",
            (cf, mg) => Norm(cf.DataType) == "varchar" && Norm(mg.DataType) == "char" && cf.Length == 36 && mg.Length == 36,
            "Guid columns: every value is exactly 36 characters, so fixed and variable length hold identical data."),
    ];


    public static IReadOnlyList<string> Differences(
        ISqlSugarClient codeFirst, ISqlSugarClient migrated, StruoDbType dbType)
    {
        var left = EntitySchemaReader.ReadAll(codeFirst, FrameworkEntityTypes.All);
        var right = EntitySchemaReader.ReadAll(migrated, FrameworkEntityTypes.All).ToDictionary(t => t.LogicalName);
        var diffs = new List<string>();
        foreach (var table in left)
        {
            var other = right[table.LogicalName];
            diffs.AddRange(CompareColumns(codeFirst, migrated, dbType, table.LogicalName, table.PhysicalName, other.PhysicalName));
            diffs.AddRange(CompareIndexes(codeFirst, migrated, dbType, table.LogicalName, table.PhysicalName, other.PhysicalName));
        }
        return diffs;
    }

    public static IReadOnlyList<string> AllTableNames(ISqlSugarClient db, string prefix) =>
        [.. EntitySchemaReader.ReadAll(db, FrameworkEntityTypes.All).Select(t => t.PhysicalName), prefix + "schema_versions"];

    private static IEnumerable<string> CompareColumns(
        ISqlSugarClient a, ISqlSugarClient b, StruoDbType dbType, string logical, string tableA, string tableB)
    {
        var left = a.DbMaintenance.GetColumnInfosByTableName(tableA, false).ToDictionary(c => c.DbColumnName.ToLowerInvariant());
        var right = b.DbMaintenance.GetColumnInfosByTableName(tableB, false).ToDictionary(c => c.DbColumnName.ToLowerInvariant());
        foreach (var name in left.Keys.Except(right.Keys))
            yield return $"{logical}.{name}: only CodeFirst has this column";
        foreach (var name in right.Keys.Except(left.Keys))
            yield return $"{logical}.{name}: only the migration has this column";
        foreach (var name in left.Keys.Intersect(right.Keys))
        foreach (var diff in ColumnAspects(left[name], right[name]))
            if (!IsAllowed(dbType, diff.Aspect, left[name], right[name]))
                yield return $"{logical}.{name}: {diff.Aspect} CodeFirst={diff.Left} migration={diff.Right}";
    }

    private static IEnumerable<(string Aspect, string Left, string Right)> ColumnAspects(DbColumnInfo x, DbColumnInfo y)
    {
        var aspects = new (string, string, string)[]
        {
            ("nullable", x.IsNullable.ToString(), y.IsNullable.ToString()),
            ("type", Norm(x.DataType), Norm(y.DataType)),
            ("length", x.Length.ToString(), y.Length.ToString()),
            ("scale", x.DecimalDigits.ToString(), y.DecimalDigits.ToString()),
            ("primaryKey", x.IsPrimarykey.ToString(), y.IsPrimarykey.ToString()),
        };
        return aspects.Where(t => t.Item2 != t.Item3);
    }

    private static string Norm(string? type) => (type ?? "").Trim().ToLowerInvariant();

    private static bool IsAllowed(StruoDbType db, string aspect, DbColumnInfo codeFirst, DbColumnInfo migrated) =>
        Allowed.Any(a => a.Db == db && a.Aspect == aspect && a.Matches(codeFirst, migrated));

    private static IEnumerable<string> CompareIndexes(
        ISqlSugarClient a, ISqlSugarClient b, StruoDbType dbType, string logical, string tableA, string tableB)
    {
        var left = IndexSets(IndexCatalog.Read(a, dbType, tableA));
        var right = IndexSets(IndexCatalog.Read(b, dbType, tableB));
        foreach (var s in left.Except(right))
            yield return $"{logical}: index {s} only on CodeFirst";
        foreach (var s in right.Except(left))
            yield return $"{logical}: index {s} only on the migration";
    }

    private static HashSet<string> IndexSets(IReadOnlyList<DbIndex> indexes) =>
        indexes.Select(i => (i.IsUnique ? "unique(" : "index(") + string.Join(",", i.Columns) + ")").ToHashSet();
}
