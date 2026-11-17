using System.Reflection;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Infrastructure.Metadata;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Migrations.Schema;

/// <summary>
/// The refusal of <c>migrate</c> on MySQL and MariaDB when the database, or a framework table that already
/// exists, does not default to utf8mb4: a column declared without a character set takes its table's default,
/// so the migrations would leave string columns outside utf8mb4 or fail on the text they hold. Pure: the
/// catalog rows come from a delegate, which <see cref="RefusalFor"/> invokes on MySQL and MariaDB only.
/// Evidence: docs/ai/decisions/mysql-string-columns-are-utf8mb4.md.
/// </summary>
internal static class Utf8Mb4Defaults
{
    private const string Utf8Mb4 = "utf8mb4";

    /// <summary>The refusal message, or null when the database may be migrated; the delegate runs only on MySQL and MariaDB.</summary>
    public static string? RefusalFor(StruoDbType dbType, string tablePrefix, Func<CatalogDefaults> read)
    {
        if (dbType != StruoDbType.MySql) return null;
        var catalog = read();
        return Refusal(catalog.Database, catalog.Schemata, catalog.Tables, tablePrefix);
    }

    /// <summary>The message naming every offending item and the statements that fix them, or null when there is none.</summary>
    public static string? Refusal(
        string database, IReadOnlyList<SchemataRow> schemata, IReadOnlyList<TablesRow> tables, string tablePrefix)
    {
        var databaseSet = schemata.Select(s => s.DefaultCharacterSetName).FirstOrDefault();
        var databaseOffends = databaseSet is not null && !IsUtf8Mb4(databaseSet);
        var core = CoreTableNames(tablePrefix);
        var offending = tables
            .Where(t => t.TableCollation is not null && core.Contains(t.TableName) && !IsUtf8Mb4(CharacterSetOf(t.TableCollation)))
            .OrderBy(t => t.TableName, StringComparer.Ordinal)
            .ToList();
        return databaseOffends || offending.Count > 0
            ? Message(database, databaseOffends ? databaseSet : null, offending)
            : null;
    }

    /// <summary>The character set a collation belongs to: the collation name up to its first underscore.</summary>
    public static string CharacterSetOf(string collation)
    {
        var end = collation.IndexOf('_');
        return end < 0 ? collation : collation[..end];
    }

    /// <summary>The prefixed names of the framework tables and of the version table.</summary>
    public static HashSet<string> CoreTableNames(string tablePrefix) =>
        new(FrameworkEntityTypes.All.Select(t => TableNaming.Apply(tablePrefix, LogicalName(t)))
            .Append(StruoVersionTableMetaData.TableNameFor(tablePrefix)), StringComparer.OrdinalIgnoreCase);

    private static string LogicalName(Type entityType) =>
        (entityType.GetCustomAttribute<SugarTable>()?.TableName ?? entityType.Name).ToLowerInvariant();

    private static bool IsUtf8Mb4(string characterSet) => characterSet.Equals(Utf8Mb4, StringComparison.OrdinalIgnoreCase);

    private static string Message(string database, string? databaseSet, IReadOnlyList<TablesRow> tables)
    {
        var lines = new List<string>
        {
            $"Migrations on MySQL and MariaDB need {Utf8Mb4} as the default character set of the database and of the framework tables; this database does not have it, so nothing was applied or changed.",
        };
        if (databaseSet is not null)
            lines.Add($"Database {Quote(database)} has the default character set {databaseSet}.");
        lines.AddRange(tables.Select(t => $"Table {Quote(t.TableName)} has the default collation {t.TableCollation}."));
        lines.Add("Run these statements, then run 'migrate' again:");
        if (databaseSet is not null)
            lines.Add($"ALTER DATABASE {Quote(database)} CHARACTER SET {Utf8Mb4};");
        lines.AddRange(tables.Select(t => $"ALTER TABLE {Quote(t.TableName)} CONVERT TO CHARACTER SET {Utf8Mb4};"));
        return string.Join(Environment.NewLine, lines);
    }

    private static string Quote(string name) => "`" + name.Replace("`", "``", StringComparison.Ordinal) + "`";
}
