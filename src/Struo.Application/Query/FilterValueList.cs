// src/Struo.Application/Query/FilterValueList.cs
using System.Collections;

namespace Struo.Application.Query;

/// <summary>
/// The elements of an <c>_in</c>/<c>_nin</c> filter value. A string is one comma-separated list (the
/// query-string form, also accepted in a JSON body); a list or array supplies its elements as they
/// are, so a comma inside an element stays inside it. The request limit and the translator both read
/// the elements through here, so they always count and use the same list.
/// </summary>
public static class FilterValueList
{
    public static IReadOnlyList<object?> Elements(object? value) => value switch
    {
        string { Length: 0 } => [],
        string text => text.Split(','),
        IEnumerable list => list.Cast<object?>().ToList(),
        _ => [value],
    };
}
