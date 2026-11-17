using SqlSugar;

namespace Struo.Infrastructure.Migrations.Schema;

/// <summary>What <see cref="DefaultCharacterSetCatalog.Read"/> returns: the connected database's name and the catalog rows of it.</summary>
internal sealed record CatalogDefaults(string Database, IReadOnlyList<SchemataRow> Schemata, IReadOnlyList<TablesRow> Tables);

/// <summary>A row of <c>information_schema.SCHEMATA</c>; it is in no entity list and carries no collection attribute.</summary>
internal sealed class SchemataRow
{
    [SugarColumn(ColumnName = "SCHEMA_NAME")] public string SchemaName { get; set; } = "";
    [SugarColumn(ColumnName = "DEFAULT_CHARACTER_SET_NAME")] public string? DefaultCharacterSetName { get; set; }
    [SugarColumn(ColumnName = "DEFAULT_COLLATION_NAME")] public string? DefaultCollationName { get; set; }
}

/// <summary>A row of <c>information_schema.TABLES</c>; it is in no entity list and carries no collection attribute.</summary>
internal sealed class TablesRow
{
    [SugarColumn(ColumnName = "TABLE_SCHEMA")] public string TableSchema { get; set; } = "";
    [SugarColumn(ColumnName = "TABLE_NAME")] public string TableName { get; set; } = "";
    [SugarColumn(ColumnName = "TABLE_COLLATION")] public string? TableCollation { get; set; }
}

/// <summary>
/// Reads the default character set and collation of the connected database and the default collation of its tables from
/// <c>information_schema.SCHEMATA</c> and <c>information_schema.TABLES</c>, on MySQL and MariaDB. Both are
/// typed queries: the catalog table name is the only string, passed to <c>AS</c>; the schema name is a bound
/// parameter. The decision is made by <see cref="Utf8Mb4Defaults"/>. Evidence:
/// docs/ai/decisions/mysql-string-columns-are-utf8mb4.md.
/// </summary>
internal static class DefaultCharacterSetCatalog
{
    public static CatalogDefaults Read(ISqlSugarClient db)
    {
        var schema = db.Ado.Connection.Database;
        var schemata = db.Queryable<SchemataRow>().AS("information_schema.SCHEMATA").Where(s => s.SchemaName == schema).ToList();
        var tables = db.Queryable<TablesRow>().AS("information_schema.TABLES").Where(t => t.TableSchema == schema).ToList();
        return new CatalogDefaults(schema, schemata, tables);
    }
}
