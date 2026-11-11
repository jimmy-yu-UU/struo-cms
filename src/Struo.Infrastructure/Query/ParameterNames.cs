// src/Struo.Infrastructure/Query/ParameterNames.cs
using SqlSugar;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

/// <summary>
/// SqlSugar names the parameter of a condition after its column and a position number, with nothing
/// between them. A column X and a column made of X and digits (X1, X12) can then end up with one
/// parameter name in one statement: SQLite, MySQL and MariaDB refuse it, PostgreSQL runs it with one
/// value bound to both. A condition list that names such a pair is refused up front.
/// </summary>
internal static class ParameterNames
{
    public static void EnsureDistinct(IEnumerable<IConditionalModel> conditions)
    {
        var columns = conditions.SelectMany(BoundColumns).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        foreach (var shorter in columns)
        {
            var clash = columns.FirstOrDefault(longer => IsNumbered(shorter, longer));
            if (clash is not null)
                throw new QueryException(
                    $"Fields '{shorter}' and '{clash}' cannot be used together in one filter: their names differ only by trailing digits.");
        }
    }

    // A subquery wrapper renders its own SQL and parameters, so its column name takes no part.
    private static IEnumerable<string> BoundColumns(IConditionalModel condition) => condition switch
    {
        ConditionalModel { CustomConditionalFunc: null, FieldName: { Length: > 0 } name } => [name],
        ConditionalCollections group => group.ConditionalList
            .Select(kv => kv.Value)
            .Where(m => m.CustomConditionalFunc is null && !string.IsNullOrEmpty(m.FieldName))
            .Select(m => m.FieldName),
        _ => [],
    };

    private static bool IsNumbered(string shorter, string longer) =>
        longer.Length > shorter.Length
        && longer.StartsWith(shorter, StringComparison.OrdinalIgnoreCase)
        && longer.AsSpan(shorter.Length).ToString().All(char.IsAsciiDigit);
}
