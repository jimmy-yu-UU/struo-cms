// src/Struo.Infrastructure/Query/LiteralLikeSubquery.cs
using System.Linq.Expressions;
using System.Reflection;
using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

/// <summary>The entity, text property and key property a typed subquery over one column is built from.</summary>
internal readonly record struct LikeSource(ISqlSugarClient Db, Type EntityType, string Property, string KeyProperty);

/// <summary>
/// A string operator as <c>key IN (SELECT key FROM entity WHERE &lt;typed Contains/StartsWith/EndsWith&gt;)</c>.
/// SqlSugar renders the expression and, for a value that holds a backslash-escaped percent sign or
/// underscore, an ESCAPE clause that a <see cref="ConditionalModel"/> cannot carry. The statement is
/// wrapped by <see cref="SubQueryConditional"/>, which gives its parameters a name of their own.
/// </summary>
internal static class LiteralLikeSubquery
{
    private static readonly BiGenericDispatcher<Func<LikeSource, QueryOperator, string, ConditionalModel>> Dispatcher =
        new(typeof(LiteralLikeSubquery), nameof(Typed), [typeof(LikeSource), typeof(QueryOperator), typeof(string)]);

    private static readonly MethodInfo ToStringMethod = typeof(object).GetMethod(nameof(ToString), Type.EmptyTypes)!;

    /// <summary>
    /// The value must already be escaped with a backslash before each backslash, percent sign and underscore.
    /// </summary>
    public static ConditionalModel Create(LikeSource source, QueryOperator op, string escapedValue) =>
        Dispatcher.For(source.EntityType, ColumnSelectorFactory.PropertyType(source.EntityType, source.KeyProperty))(
            source, op, escapedValue);

    private static ConditionalModel Typed<T, TKey>(LikeSource source, QueryOperator op, string escapedValue)
        where T : class, new()
    {
        var query = source.Db.Queryable<T>();
        if (typeof(ISoftDeletable).IsAssignableFrom(typeof(T))) query = query.ClearFilter<ISoftDeletable>();
        var sub = query
            .Where(Predicate<T>(source, op, escapedValue))
            .Select((Expression<Func<T, TKey>>)ColumnSelectorFactory.TypedSelector(typeof(T), source.KeyProperty))
            .ToSql();
        var column = source.Db.EntityMaintenance.GetDbColumnName<T>(source.KeyProperty);
        return SubQueryConditional.Wrap(SubQueryKind.In, column, sub);
    }

    // x => x.Property.Contains(value), or x.Property.ToString().Contains(value) for a column the ORM
    // stores as JSON text and that is not a string property.
    private static Expression<Func<T, bool>> Predicate<T>(LikeSource source, QueryOperator op, string escapedValue)
    {
        var x = Expression.Parameter(typeof(T), "x");
        var property = typeof(T).GetProperty(source.Property, BindingFlags.Public | BindingFlags.Instance)
            ?? throw new InvalidOperationException($"'{typeof(T).Name}' has no property '{source.Property}'.");
        Expression text = Expression.Property(x, property);
        if (property.PropertyType != typeof(string)) text = Expression.Call(text, ToStringMethod);
        var method = typeof(string).GetMethod(MethodName(op), [typeof(string)])!;
        return Expression.Lambda<Func<T, bool>>(Expression.Call(text, method, Expression.Constant(escapedValue)), x);
    }

    private static string MethodName(QueryOperator op) => op switch
    {
        QueryOperator.Contains => nameof(string.Contains),
        QueryOperator.StartsWith => nameof(string.StartsWith),
        QueryOperator.EndsWith => nameof(string.EndsWith),
        _ => throw new InvalidOperationException($"'{op}' is not a string operator."),
    };
}
