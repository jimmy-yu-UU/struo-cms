using SqlSugar;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Migrations.Schema;

/// <summary>Compares one expected table with the live table of the same name.</summary>
internal static class SchemaTableComparer
{
    public static IEnumerable<SchemaFinding> Compare(
        ISqlSugarClient db, StruoDbType dbType, ExpectedTable table, string liveName)
    {
        var live = db.DbMaintenance.GetColumnInfosByTableName(liveName, false)
            .GroupBy(c => c.DbColumnName.ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.First());

        foreach (var column in table.Columns)
        {
            if (!live.TryGetValue(column.Name, out var actual))
            {
                yield return Error(FindingKind.MissingColumn, table, column.Name, "column does not exist");
                continue;
            }
            foreach (var finding in CompareColumn(dbType, table, column, actual))
                yield return finding;
        }

        var declared = table.Columns.Select(c => c.Name).ToHashSet();
        foreach (var name in live.Keys.Where(n => !declared.Contains(n)))
            yield return new SchemaFinding(FindingSeverity.Warning, FindingKind.UndeclaredColumn, table.PhysicalName, name,
                "column exists in the database but no entity declares it");

        foreach (var finding in CompareUniqueIndexes(db, dbType, table, liveName))
            yield return finding;
    }

    public static IReadOnlyList<SchemaFinding> UnsupportedIndexWarnings(StruoDbType dbType, IReadOnlyList<ExpectedTable> tables) =>
        IndexCatalog.IsSupported(dbType) || tables.Count == 0
            ? []
            :
            [
                new SchemaFinding(FindingSeverity.Warning, FindingKind.IndexCheckUnsupported, "*", null,
                    $"unique indexes are not checked on {dbType}")
            ];

    private static IEnumerable<SchemaFinding> CompareColumn(
        StruoDbType dbType, ExpectedTable table, ExpectedColumn column, DbColumnInfo actual)
    {
        var found = $"{actual.DataType} (length {actual.Length})";
        if (!DbTypeCategories.Matches(dbType, column.Category, actual))
        {
            yield return Error(FindingKind.TypeMismatch, table, column.Name,
                $"expected {column.Category}, database has {found}");
            yield break;
        }
        if (!column.IsPrimaryKey && column.IsNullable != actual.IsNullable)
            yield return Error(FindingKind.NullabilityMismatch, table, column.Name,
                $"expected {(column.IsNullable ? "nullable" : "not null")}, database is {(actual.IsNullable ? "nullable" : "not null")}");
        if (column.Category == ColumnCategory.String && column.Length is { } length
            && !DbTypeCategories.LengthMatches(dbType, length, actual))
            yield return Error(FindingKind.LengthMismatch, table, column.Name,
                $"expected length {length}, database has {found}");
    }

    private static IEnumerable<SchemaFinding> CompareUniqueIndexes(
        ISqlSugarClient db, StruoDbType dbType, ExpectedTable table, string liveName)
    {
        var expected = table.Indexes.Where(i => i.IsUnique).ToList();
        if (expected.Count == 0 || !IndexCatalog.IsSupported(dbType)) yield break;

        var unique = IndexCatalog.Read(db, dbType, liveName).Where(i => i.IsUnique).ToList();
        foreach (var index in expected.Where(e => !unique.Any(u => SameColumns(u.Columns, e.Columns))))
            yield return Error(FindingKind.MissingUniqueIndex, table, null,
                $"no unique index on ({string.Join(", ", index.Columns)}); expected {index.Name}");
    }

    private static bool SameColumns(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Count == b.Count && a.Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(
            b.Order(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);

    private static SchemaFinding Error(FindingKind kind, ExpectedTable table, string? column, string message) =>
        new(FindingSeverity.Error, kind, table.PhysicalName, column, message);
}
