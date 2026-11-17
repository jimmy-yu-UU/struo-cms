using Struo.Application.Configuration;

namespace Struo.Infrastructure.Migrations.Schema;

/// <summary>
/// The warning for a column an entity declares as text whose live character set is not utf8mb4. The
/// message names the set, what it cannot hold and the conversion of the table. Pure: the character sets
/// come from a delegate, which <see cref="FindingsFor"/> invokes on MySQL and MariaDB only.
/// </summary>
internal static class Utf8Mb4Columns
{
    private const string Utf8Mb4 = "utf8mb4";

    /// <summary>The findings for one table; the delegate that reads the character sets runs only on MySQL and MariaDB.</summary>
    public static IEnumerable<SchemaFinding> FindingsFor(
        StruoDbType dbType, ExpectedTable table, Func<IReadOnlyList<ColumnCharacterSet>> readCharacterSets) =>
        dbType == StruoDbType.MySql ? Findings(table, readCharacterSets()) : [];

    public static IEnumerable<SchemaFinding> Findings(ExpectedTable table, IReadOnlyList<ColumnCharacterSet> live)
    {
        var sets = live.GroupBy(c => c.Column, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().CharacterSet, StringComparer.OrdinalIgnoreCase);
        return table.Columns
            .Where(c => c.Category is ColumnCategory.String or ColumnCategory.LongText)
            .Select(c => (c.Name, Set: sets.GetValueOrDefault(c.Name)))
            .Where(c => c.Set is not null && !c.Set.Equals(Utf8Mb4, StringComparison.OrdinalIgnoreCase))
            .Select(c => Finding(table, c.Name, c.Set!));
    }

    private static SchemaFinding Finding(ExpectedTable table, string column, string set) =>
        new(FindingSeverity.Warning, FindingKind.NonUtf8Mb4String, table.PhysicalName, column,
            $"database column has character set {set}, not {Utf8Mb4}; {Consequence(set)}; "
            + $"run ALTER TABLE `{table.PhysicalName}` CONVERT TO CHARACTER SET {Utf8Mb4}");

    private static string Consequence(string set) =>
        set.Equals("utf8mb3", StringComparison.OrdinalIgnoreCase) || set.Equals("utf8", StringComparison.OrdinalIgnoreCase)
            ? "characters outside the Basic Multilingual Plane, such as emoji, cannot be stored or compared"
            : $"characters that {set} does not contain cannot be stored";
}
