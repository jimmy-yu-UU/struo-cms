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
/// catalog rows come from a delegate, which <see cref="CatalogFor"/> invokes on MySQL and MariaDB only.
/// Evidence: docs/ai/decisions/mysql-string-columns-are-utf8mb4.md.
/// </summary>
internal static class Utf8Mb4Defaults
{
    private const string Utf8Mb4 = "utf8mb4";

    /// <summary>The catalog rows to judge; the delegate runs only on MySQL and MariaDB, elsewhere the result is null.</summary>
    public static CatalogDefaults? CatalogFor(StruoDbType dbType, Func<CatalogDefaults> read) =>
        dbType == StruoDbType.MySql ? read() : null;

    /// <summary>The message naming every offending item and the statements that fix them, or null when there is none.</summary>
    public static string? Refusal(CatalogDefaults catalog, string tablePrefix)
    {
        var schema = catalog.Schemata.Count > 0 ? catalog.Schemata[0] : null;
        var databaseSet = schema?.DefaultCharacterSetName;
        var databaseOffends = databaseSet is not null && !IsUtf8Mb4(databaseSet);
        var core = CoreTableNames(tablePrefix);
        var offending = catalog.Tables
            .Where(t => t.TableCollation is not null && core.Contains(t.TableName) && !IsUtf8Mb4(CharacterSetOf(t.TableCollation)))
            .OrderBy(t => t.TableName, StringComparer.Ordinal)
            .ToList();
        if (!databaseOffends && offending.Count == 0) return null;
        var databaseCollation = databaseSet is not null && !databaseOffends ? schema!.DefaultCollationName : null;
        return Message(catalog.Database, databaseOffends ? databaseSet : null, databaseCollation, offending);
    }

    /// <summary>The character set a collation belongs to: the collation name up to its first underscore.</summary>
    public static string CharacterSetOf(string collation)
    {
        var end = collation.IndexOf('_', StringComparison.Ordinal);
        return end < 0 ? collation : collation[..end];
    }

    /// <summary>The prefixed names of the framework tables and of the version table.</summary>
    public static HashSet<string> CoreTableNames(string tablePrefix) =>
        new(FrameworkEntityTypes.All.Select(t => TableNaming.Apply(tablePrefix, LogicalName(t)))
            .Append(StruoVersionTableMetaData.TableNameFor(tablePrefix)), StringComparer.OrdinalIgnoreCase);

    private static string LogicalName(Type entityType) =>
        entityType.GetCustomAttribute<SugarTable>()?.TableName ?? entityType.Name;

    private static bool IsUtf8Mb4(string characterSet) => characterSet.Equals(Utf8Mb4, StringComparison.OrdinalIgnoreCase);

    private static string Message(string database, string? databaseSet, string? databaseCollation, IReadOnlyList<TablesRow> tables)
    {
        var lines = new List<string>
        {
            $"migrations on MySQL and MariaDB need {Utf8Mb4} as the default character set of the database and the framework tables, "
            + "and the items below do not have it, so nothing was applied or changed.",
        };
        if (databaseSet is not null)
            lines.Add($"Database {Quote(database)} has the default character set {databaseSet}.");
        lines.AddRange(tables.Select(t => $"Table {Quote(t.TableName)} has the default collation {t.TableCollation}."));
        lines.Add("Run these statements, then run 'migrate' again:");
        if (databaseSet is not null)
            lines.Add($"ALTER DATABASE {Quote(database)} CHARACTER SET {Utf8Mb4};");
        var collate = databaseCollation is null ? "" : $" COLLATE {databaseCollation}";
        lines.AddRange(tables.Select(t => $"ALTER TABLE {Quote(t.TableName)} CONVERT TO CHARACTER SET {Utf8Mb4}{collate};"));
        lines.Add($"Convert tables of your own that do not default to {Utf8Mb4} with ALTER TABLE ... CONVERT TO CHARACTER SET {Utf8Mb4}{collate};");
        lines.Add("'migrate:check' lists their columns.");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>A MySQL identifier in back-ticks, with back-ticks inside it doubled.</summary>
    public static string Quote(string name) => "`" + name.Replace("`", "``", StringComparison.Ordinal) + "`";
}
