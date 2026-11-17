namespace Struo.Infrastructure.Migrations.Schema;

/// <summary>
/// The warning for a column an entity declares as text whose live character set is not utf8mb4: a
/// character outside the Basic Multilingual Plane, an emoji for example, can neither be stored in it nor
/// compared with it. Pure: the character sets come from <see cref="CharacterSetCatalog"/>.
/// </summary>
internal static class Utf8Mb4Columns
{
    private const string Utf8Mb4 = "utf8mb4";

    public static IEnumerable<SchemaFinding> Findings(ExpectedTable table, IReadOnlyList<ColumnCharacterSet> live)
    {
        var sets = live.GroupBy(c => c.Column, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First().CharacterSet, StringComparer.OrdinalIgnoreCase);
        foreach (var column in table.Columns.Where(c => c.Category is ColumnCategory.String or ColumnCategory.LongText))
        {
            if (!sets.TryGetValue(column.Name, out var set) || set is null
                || set.Equals(Utf8Mb4, StringComparison.OrdinalIgnoreCase)) continue;
            yield return new SchemaFinding(FindingSeverity.Warning, FindingKind.NonUtf8Mb4String, table.PhysicalName, column.Name,
                $"database column has character set {set}, not {Utf8Mb4}; characters outside the Basic Multilingual Plane, such as emoji, cannot be stored or compared; convert the column to {Utf8Mb4}");
        }
    }
}
