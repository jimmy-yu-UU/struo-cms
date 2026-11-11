// src/Struo.Infrastructure/Query/ParameterNames.cs
using System.Reflection;
using SqlSugar;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

/// <summary>
/// SqlSugar names the parameter of a condition after its column and a position number, with nothing
/// between them. A column X and a column made of X and digits (X1, X12) can then end up with one
/// parameter name in one statement: SQLite, MySQL and MariaDB refuse it, PostgreSQL runs it with one
/// value bound to both. A condition list whose column names could meet that way is rendered once on a
/// copy, without running it, and refused only when the rendered parameters really repeat a name.
/// </summary>
internal static class ParameterNames
{
    private static readonly GenericDispatcher<Func<ISqlSugarClient, List<IConditionalModel>, List<string>>> RenderedNamesDispatcher =
        new(typeof(ParameterNames), nameof(RenderedNames), [typeof(ISqlSugarClient), typeof(List<IConditionalModel>)]);

    private static readonly PropertyInfo[] ModelProperties =
        typeof(ConditionalModel).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p is { CanRead: true, CanWrite: true } && p.GetIndexParameters().Length == 0).ToArray();

    public static void EnsureDistinct(ISqlSugarClient db, Func<Type> entityType, IReadOnlyList<IConditionalModel> conditions)
    {
        var columns = conditions.SelectMany(BoundColumns).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var pair = columns
            .SelectMany(shorter => columns.Where(longer => IsNumbered(shorter, longer)).Select(longer => (shorter, longer)))
            .FirstOrDefault();
        if (pair == default) return;

        var type = entityType();
        var copy = conditions.Select(Copy).ToList();
        var repeated = RenderedNamesDispatcher.For(type)(db, copy)
            .GroupBy(n => n, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1);
        if (!repeated) return;

        var labels = FieldLabels(db, type);
        throw new QueryException(
            $"Fields '{Label(labels, pair.shorter)}' and '{Label(labels, pair.longer)}' cannot be used together in one query: their names differ only by trailing digits.");
    }

    private static List<string> RenderedNames<T>(ISqlSugarClient db, List<IConditionalModel> conditions) where T : class, new() =>
        db.Queryable<T>().Where(conditions).ToSql().Value.Select(p => p.ParameterName.TrimStart('@', ':', '?')).ToList();

    // The API names a field in camelCase; the condition holds the database column.
    private static Dictionary<string, string> FieldLabels(ISqlSugarClient db, Type entityType) =>
        db.EntityMaintenance.GetEntityInfo(entityType).Columns
            .Where(c => !c.IsIgnore && !string.IsNullOrEmpty(c.DbColumnName))
            .GroupBy(c => c.DbColumnName, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => char.ToLowerInvariant(g.First().PropertyName[0]) + g.First().PropertyName[1..],
                StringComparer.OrdinalIgnoreCase);

    private static string Label(Dictionary<string, string> labels, string column) =>
        labels.TryGetValue(column, out var field) ? field : column;

    // The copy is what gets rendered: SqlSugar rewrites the value of some conditions while it renders them.
    private static IConditionalModel Copy(IConditionalModel condition) => condition switch
    {
        ConditionalModel model => CopyModel(model),
        ConditionalCollections group => new ConditionalCollections
        {
            ConditionalList = group.ConditionalList
                .Select(kv => new KeyValuePair<WhereType, ConditionalModel>(kv.Key, CopyModel(kv.Value))).ToList(),
        },
        _ => condition,
    };

    private static ConditionalModel CopyModel(ConditionalModel model)
    {
        var copy = new ConditionalModel();
        foreach (var property in ModelProperties) property.SetValue(copy, property.GetValue(model));
        return copy;
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
