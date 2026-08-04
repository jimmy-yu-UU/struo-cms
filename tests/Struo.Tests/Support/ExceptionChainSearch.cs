namespace Struo.Tests.Support;

/// <summary>
/// Walks an exception's <see cref="Exception.InnerException"/> chain, fanning out across
/// <see cref="AggregateException.InnerExceptions"/> at each step, looking for the first exception
/// assignable to <typeparamref name="T"/>. Extracted from two call sites
/// (<c>OptionsValidationTests</c>, <c>ColumnTypeMapTests</c>) that independently needed the same
/// traversal because the library under test (the generic host, SqlSugar) may wrap the exception a
/// hook or validator actually throws.
/// </summary>
internal static class ExceptionChainSearch
{
    public static T? FindInner<T>(Exception? ex) where T : Exception
    {
        while (ex is not null)
        {
            if (ex is T match) return match;
            if (ex is AggregateException agg)
            {
                foreach (var inner in agg.InnerExceptions)
                {
                    var found = FindInner<T>(inner);
                    if (found is not null) return found;
                }
            }
            ex = ex.InnerException;
        }
        return null;
    }
}
