using SqlSugar;
using Struo.Application.Configuration;

namespace Struo.Infrastructure.Migrations.Schema;

/// <summary>
/// Reads index definitions (name, uniqueness, key columns in order) from each backend's catalog.
/// SqlSugar cannot introspect these, so each backend gets one read-only query that takes the table name
/// as a parameter. Primary-key indexes are included. Oracle is unsupported.
/// </summary>
internal static class IndexCatalog
{
    private const string Postgres = """
        SELECT ic.relname AS name,
               CASE WHEN i.indisunique THEN 1 ELSE 0 END AS is_unique,
               a.attname AS column_name,
               CAST(k.ord AS integer) AS ordinal
        FROM pg_index i
        JOIN pg_class t ON t.oid = i.indrelid
        JOIN pg_namespace n ON n.oid = t.relnamespace
        JOIN pg_class ic ON ic.oid = i.indexrelid
        CROSS JOIN LATERAL unnest(i.indkey) WITH ORDINALITY AS k(attnum, ord)
        JOIN pg_attribute a ON a.attrelid = t.oid AND a.attnum = k.attnum
        WHERE n.nspname = current_schema() AND t.relname = @table
          AND k.attnum > 0 AND k.ord <= i.indnkeyatts
        ORDER BY ic.relname, k.ord
        """;

    private const string SqlServer = """
        SELECT i.name AS name,
               CASE WHEN i.is_unique = 1 THEN 1 ELSE 0 END AS is_unique,
               c.name AS column_name,
               CAST(ic.key_ordinal AS int) AS ordinal
        FROM sys.indexes i
        JOIN sys.index_columns ic ON ic.object_id = i.object_id AND ic.index_id = i.index_id
        JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
        WHERE i.object_id = OBJECT_ID(@table) AND i.type > 0 AND ic.is_included_column = 0
        ORDER BY i.name, ic.key_ordinal
        """;

    private const string MySql = """
        SELECT INDEX_NAME AS name,
               CASE WHEN NON_UNIQUE = 0 THEN 1 ELSE 0 END AS is_unique,
               COLUMN_NAME AS column_name,
               CAST(SEQ_IN_INDEX AS SIGNED) AS ordinal
        FROM information_schema.STATISTICS
        WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @table AND COLUMN_NAME IS NOT NULL
        ORDER BY INDEX_NAME, SEQ_IN_INDEX
        """;

    private const string Sqlite = """
        SELECT il.name AS name, il."unique" AS is_unique, ii.name AS column_name, ii.seqno AS ordinal
        FROM pragma_index_list(@table) il
        JOIN pragma_index_info(il.name) ii
        ORDER BY il.name, ii.seqno
        """;

    public static bool IsSupported(StruoDbType db) => Sql(db) is not null;

    internal static string? Sql(StruoDbType db) => db switch
    {
        StruoDbType.PostgreSQL => Postgres,
        StruoDbType.SqlServer => SqlServer,
        StruoDbType.MySql => MySql,
        StruoDbType.Sqlite => Sqlite,
        _ => null,
    };

    public static IReadOnlyList<DbIndex> Read(ISqlSugarClient db, StruoDbType dbType, string table)
    {
        var sql = Sql(dbType) ?? throw new NotSupportedException(
            $"Index introspection is not supported for {dbType}.");
        var rows = db.Ado.SqlQuery<IndexRow>(sql, new SugarParameter("@table", table));
        return rows
            .GroupBy(r => r.Name)
            .Select(g => new DbIndex(
                g.Key,
                g.First().IsUnique == 1,
                g.OrderBy(r => r.Ordinal).Select(r => r.ColumnName.ToLowerInvariant()).ToArray()))
            .OrderBy(i => i.Name, StringComparer.Ordinal)
            .ToArray();
    }

    private sealed class IndexRow
    {
        public string Name { get; set; } = "";
        [SugarColumn(ColumnName = "is_unique")]
        public int IsUnique { get; set; }

        [SugarColumn(ColumnName = "column_name")]
        public string ColumnName { get; set; } = "";

        public int Ordinal { get; set; }
    }
}
