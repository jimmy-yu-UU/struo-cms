using System.Reflection;
using SqlSugar;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Migrations.Schema;

/// <summary>Reads the schema an entity type declares from SqlSugar's post-convention entity metadata.</summary>
public static class EntitySchemaReader
{
    private const int DefaultStringLength = 255;

    public static ExpectedTable Read(ISqlSugarClient db, Type entityType)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(entityType);

        var dbType = db.CurrentConnectionConfig.DbType;
        var info = db.EntityMaintenance.GetEntityInfo(entityType);
        var columns = info.Columns.Where(c => !c.IsIgnore).ToList();
        var physical = info.DbTableName.ToLowerInvariant();
        var logical = LogicalNameOf(entityType);

        var indexes = DeclaredIndexes(entityType, columns, physical)
            .Concat(GroupedUniqueIndexes(columns))
            .ToList();
        var isSidecar = indexes.Any(i => i.IsUnique && i.Name == TranslationSidecarIndexPolicy.IndexNameFor(logical));

        return new ExpectedTable(
            entityType,
            physical,
            logical,
            TableNaming.IsFrameworkTable(entityType),
            columns.Select(c => ToColumn(c, dbType)).ToList(),
            indexes,
            isSidecar ? logical : null);
    }

    public static IReadOnlyList<ExpectedTable> ReadAll(ISqlSugarClient db, IEnumerable<Type> entityTypes)
    {
        ArgumentNullException.ThrowIfNull(entityTypes);
        return entityTypes
            .Select(t => Read(db, t))
            .OrderBy(t => t.PhysicalName, StringComparer.Ordinal)
            .ToList();
    }

    private static string LogicalNameOf(Type entityType)
    {
        var name = entityType.GetCustomAttribute<SugarTable>()?.TableName;
        return (string.IsNullOrWhiteSpace(name) ? entityType.Name : name).ToLowerInvariant();
    }

    private static ExpectedColumn ToColumn(EntityColumnInfo column, SqlSugar.DbType dbType)
    {
        var category = CategoryOf(column, dbType);
        int? length = category == ColumnCategory.String
            ? (column.Length > 0 ? column.Length : DefaultStringLength)
            : null;
        return new ExpectedColumn(
            column.DbColumnName.ToLowerInvariant(),
            category,
            column.IsNullable,
            length,
            column.IsPrimarykey,
            column.IsIdentity,
            column.IsJson);
    }

    private static ColumnCategory CategoryOf(EntityColumnInfo column, SqlSugar.DbType dbType)
    {
        var dataType = column.DataType;
        if (column.IsJson || Matches(dataType, ColumnTypeMap.For(ColumnShape.LongText, dbType)))
            return ColumnCategory.LongText;
        if (Matches(dataType, ColumnTypeMap.For(ColumnShape.TimestampWithTimeZone, dbType)))
            return ColumnCategory.DateTimeWithTimeZone;

        var type = Nullable.GetUnderlyingType(column.UnderType) ?? column.UnderType;
        if (type.IsEnum) return ColumnCategory.Integer;
        if (type == typeof(string)) return ColumnCategory.String;
        if (type == typeof(int) || type == typeof(short) || type == typeof(byte)) return ColumnCategory.Integer;
        if (type == typeof(long)) return ColumnCategory.BigInteger;
        if (type == typeof(decimal) || type == typeof(double) || type == typeof(float)) return ColumnCategory.Decimal;
        if (type == typeof(bool)) return ColumnCategory.Boolean;
        if (type == typeof(Guid)) return ColumnCategory.Guid;
        if (type == typeof(DateTime)) return ColumnCategory.DateTime;
        if (type == typeof(DateTimeOffset)) return ColumnCategory.DateTimeWithTimeZone;
        if (type == typeof(byte[])) return ColumnCategory.Binary;
        return ColumnCategory.Other;
    }

    private static bool Matches(string? dataType, string literal) =>
        string.Equals(dataType, literal, StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<ExpectedIndex> DeclaredIndexes(
        Type entityType, IReadOnlyList<EntityColumnInfo> columns, string physicalName)
    {
        var columnNames = columns.ToDictionary(c => c.PropertyName, c => c.DbColumnName.ToLowerInvariant(), StringComparer.Ordinal);
        foreach (var attribute in entityType.GetCustomAttributes<SugarIndexAttribute>())
        {
            yield return new ExpectedIndex(
                attribute.IndexName.Replace("{table}", physicalName, StringComparison.Ordinal),
                attribute.IndexFields.Keys.Select(p => columnNames.GetValueOrDefault(p, p.ToLowerInvariant())).ToList(),
                attribute.IsUnique);
        }
    }

    // Unique groups attached to columns, including the (fk, locale) group the sidecar policy adds.
    private static IEnumerable<ExpectedIndex> GroupedUniqueIndexes(IReadOnlyList<EntityColumnInfo> columns) =>
        columns
            .SelectMany(c => (c.UIndexGroupNameList ?? []).Select(g => (Group: g, Column: c.DbColumnName.ToLowerInvariant())))
            .GroupBy(x => x.Group, StringComparer.Ordinal)
            .Select(g => new ExpectedIndex(g.Key, g.Select(x => x.Column).ToList(), true));
}
