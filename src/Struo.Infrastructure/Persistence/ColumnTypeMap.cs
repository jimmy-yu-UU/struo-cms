using SqlSugar;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Resolves a dialect-neutral <see cref="ColumnShape"/> to a per-backend column type literal — the only
/// other site in <c>src/</c> is <see cref="SqlSugarClientFactory"/>'s <c>ApplySqliteIdentityColumnRewrite</c>.
/// Read in upstream SqlSugar source: neither <c>GetSize</c> nor <c>ConvertCreateColumnInfo</c> suffixes
/// the two already-parenthesised literals below, ruling out a double-suffixed DDL; not run live on
/// MySQL, SQL Server, or Oracle. Reversing MySQL's <c>longtext</c> risks truncation; reversing a
/// parenthesised literal risks the double suffix. Full write-up: docs/ai/decisions/column-type-map-per-backend-literals.md
/// </summary>
internal static class ColumnTypeMap
{
    public static string For(ColumnShape shape, DbType dbType) => shape switch
    {
        ColumnShape.LongText => dbType switch
        {
            DbType.PostgreSQL or DbType.Sqlite => "text",
            // MySQL's TEXT caps at 65,535 bytes — not unbounded, unlike every other mapping here.
            // A full item Revision.Snapshot or a realistic RichText/Markdown/Json body can exceed
            // that (error 1406 in strict mode, silent truncation otherwise). LONGTEXT (up to 4 GiB)
            // is the conventional MySQL choice for unbounded text.
            DbType.MySql => "longtext",
            DbType.SqlServer => "nvarchar(max)",
            DbType.Oracle => "clob",
            _ => throw new ArgumentOutOfRangeException(nameof(dbType), dbType, "Unmapped DbType for ColumnShape.LongText."),
        },
        ColumnShape.TimestampWithTimeZone => dbType switch
        {
            DbType.PostgreSQL => "timestamptz",
            // MySQL's TIMESTAMP is range-limited (1970-2038); DATETIME(6) storing UTC is the
            // conventional choice for an instant.
            DbType.MySql => "datetime(6)",
            DbType.SqlServer => "datetimeoffset",
            DbType.Oracle => "timestamp with time zone",
            // SQLite does not validate declared type names (it derives a storage affinity from
            // substring matches), so this literal is accepted as-is — preserving exactly the
            // column type the existing test suite has always created.
            DbType.Sqlite => "timestamptz",
            _ => throw new ArgumentOutOfRangeException(nameof(dbType), dbType, "Unmapped DbType for ColumnShape.TimestampWithTimeZone."),
        },
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unmapped ColumnShape."),
    };
}
