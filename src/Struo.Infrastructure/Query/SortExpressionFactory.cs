// src/Struo.Infrastructure/Query/SortExpressionFactory.cs
using System.Linq.Expressions;
using System.Reflection;
using SqlSugar;

namespace Struo.Infrastructure.Query;

/// <summary>One to-one relation hop: the target entity, its PK property, and the FK property on the previous entity.</summary>
internal sealed record SortHop(Type Target, string IdProperty, string FkOnPrevious);

/// <summary>
/// Builds the typed sort-key lambdas SqlSugar renders into ORDER BY. A plain column is
/// <c>it =&gt; (object)it.Prop</c> (<see cref="ColumnSelectorFactory.BoxedSelector"/>); the two
/// subquery forms below are <c>SqlFunc.Subqueryable</c> trees built from property names, so no SQL text
/// is assembled and the locale reaches the command as a bound parameter.
/// </summary>
internal static class SortExpressionFactory
{
    // The locale is read through a field on a constant holder: SqlSugar binds that shape as a parameter.
    private sealed class ValueBox(string value)
    {
        public readonly string Value = value;
    }

    /// <summary>
    /// it =&gt; Subqueryable&lt;TTrans&gt;().Where(t =&gt; t.Fk == it.Id &amp;&amp; t.Locale == locale).Select(t =&gt; t.Field)
    /// </summary>
    public static LambdaExpression TranslatableSubquery(
        Type entityType, string idProperty, Type translationType,
        string fkProperty, string localeProperty, string fieldProperty, string locale)
    {
        var it = Expression.Parameter(entityType, "it");
        var t = Expression.Parameter(translationType, "t");
        var localeValue = Expression.Field(Expression.Constant(new ValueBox(locale)), nameof(ValueBox.Value));
        var predicate = Expression.AndAlso(
            Eq(Expression.Property(t, fkProperty), Expression.Property(it, idProperty)),
            Expression.Equal(Expression.Property(t, localeProperty), localeValue));
        var select = SelectWhere(translationType, t, predicate, Expression.Property(t, fieldProperty));
        return Boxed(entityType, select, it);
    }

    /// <summary>
    /// A to-one path as nested subqueries, one per hop, innermost first:
    /// it =&gt; Sub&lt;T2&gt;().Where(x2 =&gt; x2.Id == Sub&lt;T1&gt;().Where(x1 =&gt; x1.Id == it.Fk0).Select(x1 =&gt; x1.Fk1)).Select(x2 =&gt; x2.Leaf)
    /// </summary>
    public static LambdaExpression RelationPath(Type entityType, IReadOnlyList<SortHop> hops, string leafProperty)
    {
        var it = Expression.Parameter(entityType, "it");
        Expression link = Expression.Property(it, hops[0].FkOnPrevious);
        for (var k = 0; k < hops.Count; k++)
        {
            var x = Expression.Parameter(hops[k].Target, "x" + (k + 1));
            var picked = k == hops.Count - 1
                ? Expression.Property(x, leafProperty)
                : Expression.Property(x, hops[k + 1].FkOnPrevious);
            link = SelectWhere(hops[k].Target, x, Eq(Expression.Property(x, hops[k].IdProperty), link), picked);
        }
        return Boxed(entityType, link, it);
    }

    // SqlFunc.Subqueryable<T>().Where(param => predicate).Select(param => picked)
    private static MethodCallExpression SelectWhere(Type type, ParameterExpression param, Expression predicate, Expression picked)
    {
        var sub = Expression.Call(SubqueryableOf(type));
        var where = Expression.Call(sub, SubWhere(sub.Type, type), Expression.Lambda(predicate, param));
        return Expression.Call(where, SubSelect(where.Type, picked.Type), Expression.Lambda(picked, param));
    }

    private static LambdaExpression Boxed(Type entityType, Expression body, ParameterExpression it) =>
        Expression.Lambda(typeof(Func<,>).MakeGenericType(entityType, typeof(object)), Expression.Convert(body, typeof(object)), it);

    // A nullable FK compares to a non-nullable PK through a lifted Convert.
    private static BinaryExpression Eq(Expression l, Expression r)
    {
        if (l.Type == r.Type) return Expression.Equal(l, r);
        if (Nullable.GetUnderlyingType(l.Type) == r.Type) return Expression.Equal(l, Expression.Convert(r, l.Type));
        if (Nullable.GetUnderlyingType(r.Type) == l.Type) return Expression.Equal(Expression.Convert(l, r.Type), r);
        throw new InvalidOperationException($"Cannot compare '{l.Type.Name}' with '{r.Type.Name}'.");
    }

    private static MethodInfo SubqueryableOf(Type type) =>
        typeof(SqlFunc).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(SqlFunc.Subqueryable) && m.IsGenericMethodDefinition
                && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 0)
            .MakeGenericMethod(type);

    private static MethodInfo SubWhere(Type subqueryType, Type entityType) =>
        subqueryType.GetMethods().First(m => m.Name == "Where" && !m.IsGenericMethodDefinition
            && m.GetParameters().Length == 1
            && m.GetParameters()[0].ParameterType == typeof(Func<,>).MakeGenericType(entityType, typeof(bool)));

    private static MethodInfo SubSelect(Type subqueryType, Type resultType) =>
        subqueryType.GetMethods().First(m => m.Name == "Select" && m.IsGenericMethodDefinition
            && m.GetGenericArguments().Length == 1 && m.GetParameters().Length == 1)
            .MakeGenericMethod(resultType);
}
