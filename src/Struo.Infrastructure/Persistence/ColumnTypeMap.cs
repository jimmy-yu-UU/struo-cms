using SqlSugar;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Resolves a dialect-neutral <see cref="ColumnShape"/> to the concrete column type literal for a
/// backend. This is the single place a vendor type name may appear in <c>src/</c>.
///
/// <para>
/// PostgreSQL and SQLite are this repository's verified backends; the other three mappings are
/// chosen to be syntactically valid and semantically reasonable so that CodeFirst table creation
/// succeeds, and are documented as unverified in <c>AGENTS.md</c>.
/// </para>
/// </summary>
internal static class ColumnTypeMap
{
    public static string For(ColumnShape shape, DbType dbType) => shape switch
    {
        ColumnShape.LongText => dbType switch
        {
            DbType.PostgreSQL or DbType.MySql or DbType.Sqlite => "text",
            DbType.SqlServer => "nvarchar(max)",
            DbType.Oracle => "clob",
            _ => "text",
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
            _ => "timestamptz",
        },
        _ => throw new ArgumentOutOfRangeException(nameof(shape), shape, "Unmapped ColumnShape."),
    };
}
