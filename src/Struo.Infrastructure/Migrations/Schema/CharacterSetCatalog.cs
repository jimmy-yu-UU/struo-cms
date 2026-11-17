using SqlSugar;

namespace Struo.Infrastructure.Migrations.Schema;

/// <summary>
/// Reads the character set of each column of one table from <c>information_schema.COLUMNS</c> of the
/// connected database, on MySQL and MariaDB. It is a typed query: the catalog table name is the only
/// string, passed to <c>AS</c>; the schema and the table name are bound parameters. Evidence:
/// docs/ai/decisions/mysql-string-columns-are-utf8mb4.md.
/// </summary>
internal static class CharacterSetCatalog
{
    private const string CatalogTable = "information_schema.COLUMNS";

    public static IReadOnlyList<ColumnCharacterSet> Read(ISqlSugarClient db, string table)
    {
        var schema = db.Ado.Connection.Database;
        return db.Queryable<InformationSchemaColumn>()
            .AS(CatalogTable)
            .Where(c => c.TableSchema == schema && c.TableName == table)
            .ToList()
            .Select(c => new ColumnCharacterSet(c.ColumnName, c.CharacterSetName))
            .ToList();
    }

    /// <summary>Read-only projection of the catalog table. It carries no <c>[SugarTable]</c>, so the entity scan never treats it as a table.</summary>
    internal sealed class InformationSchemaColumn
    {
        [SugarColumn(ColumnName = "TABLE_SCHEMA")] public string TableSchema { get; set; } = "";
        [SugarColumn(ColumnName = "TABLE_NAME")] public string TableName { get; set; } = "";
        [SugarColumn(ColumnName = "COLUMN_NAME")] public string ColumnName { get; set; } = "";
        [SugarColumn(ColumnName = "CHARACTER_SET_NAME")] public string? CharacterSetName { get; set; }
    }
}
