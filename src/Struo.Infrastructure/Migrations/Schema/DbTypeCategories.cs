using SqlSugar;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Migrations.Schema;

/// <summary>Maps the type a backend reports for a live column to the categories an entity column can have.</summary>
internal static class DbTypeCategories
{
    private const int SqlServerMax = -1;

    public static bool Matches(StruoDbType db, ColumnCategory expected, DbColumnInfo actual) =>
        expected == ColumnCategory.Other || Of(db, actual).Contains(expected);

    public static bool LengthMatches(StruoDbType db, int expected, DbColumnInfo actual) =>
        actual.Length <= 0 || actual.Length == expected || (db == StruoDbType.SqlServer && actual.Length == SqlServerMax);

    private static ColumnCategory[] Of(StruoDbType db, DbColumnInfo actual)
    {
        var type = (actual.DataType ?? "").Trim().ToLowerInvariant();
        return db switch
        {
            StruoDbType.PostgreSQL => Postgres(type),
            StruoDbType.SqlServer => SqlServer(type, actual.Length),
            StruoDbType.MySql => MySql(type, actual.Length),
            StruoDbType.Sqlite => Sqlite(type),
            _ => [],
        };
    }

    private static ColumnCategory[] Postgres(string type) => type switch
    {
        "varchar" or "character varying" or "bpchar" or "character" or "char" => [ColumnCategory.String],
        "text" => [ColumnCategory.LongText],
        "int2" or "int4" or "smallint" or "integer" => [ColumnCategory.Integer],
        "int8" or "bigint" => [ColumnCategory.BigInteger],
        "numeric" or "decimal" => [ColumnCategory.Decimal],
        "float4" or "float8" or "real" or "double precision" => [ColumnCategory.Double],
        "bool" or "boolean" => [ColumnCategory.Boolean],
        "uuid" => [ColumnCategory.Guid],
        "timestamp" or "timestamp without time zone" => [ColumnCategory.DateTime],
        "timestamptz" or "timestamp with time zone" => [ColumnCategory.DateTimeWithTimeZone],
        "bytea" => [ColumnCategory.Binary],
        _ => [],
    };

    private static ColumnCategory[] SqlServer(string type, int length) => type switch
    {
        "nvarchar" or "varchar" or "nchar" or "char" =>
            [length == SqlServerMax ? ColumnCategory.LongText : ColumnCategory.String],
        "ntext" or "text" => [ColumnCategory.LongText],
        "int" or "smallint" or "tinyint" => [ColumnCategory.Integer],
        "bigint" => [ColumnCategory.BigInteger],
        "decimal" or "numeric" or "money" => [ColumnCategory.Decimal],
        "float" or "real" => [ColumnCategory.Double],
        "bit" => [ColumnCategory.Boolean],
        "uniqueidentifier" => [ColumnCategory.Guid],
        "datetime" or "datetime2" => [ColumnCategory.DateTime],
        "datetimeoffset" => [ColumnCategory.DateTimeWithTimeZone],
        "varbinary" or "binary" or "image" => [ColumnCategory.Binary],
        _ => [],
    };

    private static ColumnCategory[] MySql(string type, int length) => type switch
    {
        "varchar" or "char" => length == 36 ? [ColumnCategory.String, ColumnCategory.Guid] : [ColumnCategory.String],
        "longtext" or "mediumtext" or "text" => [ColumnCategory.LongText],
        "tinyint" => length == 1 ? [ColumnCategory.Boolean] : [ColumnCategory.Integer],
        "int" or "smallint" or "mediumint" => [ColumnCategory.Integer],
        "bigint" => [ColumnCategory.BigInteger],
        "decimal" => [ColumnCategory.Decimal],
        "double" or "float" => [ColumnCategory.Double],
        "bit" => [ColumnCategory.Boolean],
        "datetime" or "timestamp" => [ColumnCategory.DateTime, ColumnCategory.DateTimeWithTimeZone],
        "blob" or "longblob" or "mediumblob" or "varbinary" or "binary" => [ColumnCategory.Binary],
        _ => [],
    };

    // SQLite derives a column affinity from substrings of the declared type; any category that
    // affinity can store is accepted.
    private static ColumnCategory[] Sqlite(string type)
    {
        if (type.Contains("int", StringComparison.Ordinal))
            return [ColumnCategory.Integer, ColumnCategory.BigInteger, ColumnCategory.Boolean];
        if (type.Contains("char", StringComparison.Ordinal) || type.Contains("clob", StringComparison.Ordinal)
            || type.Contains("text", StringComparison.Ordinal))
            return [ColumnCategory.String, ColumnCategory.LongText, ColumnCategory.Guid, ColumnCategory.DateTime, ColumnCategory.DateTimeWithTimeZone];
        if (type.Length == 0 || type.Contains("blob", StringComparison.Ordinal))
            return [ColumnCategory.Binary];
        if (type.Contains("real", StringComparison.Ordinal) || type.Contains("floa", StringComparison.Ordinal)
            || type.Contains("doub", StringComparison.Ordinal))
            return [ColumnCategory.Double, ColumnCategory.Decimal];
        return [ColumnCategory.Decimal, ColumnCategory.Double, ColumnCategory.Boolean, ColumnCategory.Guid, ColumnCategory.DateTime, ColumnCategory.DateTimeWithTimeZone];
    }
}
