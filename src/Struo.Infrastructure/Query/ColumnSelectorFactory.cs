// src/Struo.Infrastructure/Query/ColumnSelectorFactory.cs
using System.Linq.Expressions;
using System.Reflection;
using SqlSugar;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

internal static class ColumnSelectorFactory
{
    // x => x.Prop with the property's REAL CLR type — SqlSugar's Select() only turns this shape into a
    // one-column projection (a boxed Convert body yields an empty/garbage result).
    public static LambdaExpression TypedSelector(Type entityType, string clrProperty)
    {
        var p = Expression.Parameter(entityType, "x");
        var prop = Property(entityType, clrProperty);
        return Expression.Lambda(typeof(Func<,>).MakeGenericType(entityType, prop.PropertyType), Expression.Property(p, prop), p);
    }

    public static Type PropertyType(Type entityType, string clrProperty) => Property(entityType, clrProperty).PropertyType;

    // x => (object)x.Prop — the boxed group-key / order-key shape SqlSugar's GroupBy/OrderBy expect
    // when the key type varies at runtime (facet value columns are only known by name, not compile time).
    public static LambdaExpression BoxedSelector(Type entityType, string clrProperty)
    {
        var p = Expression.Parameter(entityType, "x");
        var body = Expression.Convert(Expression.Property(p, Property(entityType, clrProperty)), typeof(object));
        return Expression.Lambda(typeof(Func<,>).MakeGenericType(entityType, typeof(object)), body, p);
    }

    // x => (object)SqlFunc.AggregateCount(x.P) / AggregateDistinctCount(x.P) — the boxed count
    // SqlSugar renders in both the facet's Select projection and its ORDER BY count(...) DESC.
    public static LambdaExpression CountSelector(Type entityType, string countProperty, bool distinct)
    {
        var p = Expression.Parameter(entityType, "x");
        var body = Expression.Convert(CountCall(p, Property(entityType, countProperty), distinct), typeof(object));
        return Expression.Lambda(typeof(Func<,>).MakeGenericType(entityType, typeof(object)), body, p);
    }

    public static Type FacetRowType(Type entityType, string valueProperty) =>
        typeof(FacetRow<>).MakeGenericType(Property(entityType, valueProperty).PropertyType);

    // x => new FacetRow<TValue> { Value = x.Value, Count = (long)SqlFunc.AggregateCount(x.Count) } —
    // TValue is the value property's own CLR type (SqlSugar maps Value straight from the group key).
    public static LambdaExpression FacetProjection(Type entityType, string valueProperty, string countProperty, bool distinct)
    {
        var p = Expression.Parameter(entityType, "x");
        var value = Property(entityType, valueProperty);
        var rowType = typeof(FacetRow<>).MakeGenericType(value.PropertyType);
        var body = Expression.MemberInit(Expression.New(rowType),
            Expression.Bind(rowType.GetProperty(nameof(FacetRow<object>.Value))!, Expression.Property(p, value)),
            Expression.Bind(rowType.GetProperty(nameof(FacetRow<object>.Count))!,
                Expression.Convert(CountCall(p, Property(entityType, countProperty), distinct), typeof(long))));
        return Expression.Lambda(typeof(Func<,>).MakeGenericType(entityType, rowType), body, p);
    }

    // x => new AggregateRow { V0 = (object)SqlFunc.AggregateSum(x.P0), V1 = ... } — one SqlFunc.Aggregate*
    // call per requested (op, property) slot, boxed into AggregateRow's object? properties.
    public static LambdaExpression AggregateProjection(Type entityType, IReadOnlyList<(AggregateOp Op, string Property)> slots)
    {
        if (slots.Count > AggregateRow.SlotCount)
            throw new ArgumentOutOfRangeException(nameof(slots), $"At most {AggregateRow.SlotCount} aggregate slots per projection.");
        var p = Expression.Parameter(entityType, "x");
        var bindings = slots.Select((s, i) =>
        {
            var prop = Property(entityType, s.Property);
            var call = Expression.Call(Aggregate(AggregateMethodName(s.Op), prop.PropertyType), Expression.Property(p, prop));
            return (MemberBinding)Expression.Bind(typeof(AggregateRow).GetProperty("V" + i)!, Expression.Convert(call, typeof(object)));
        });
        var body = Expression.MemberInit(Expression.New(typeof(AggregateRow)), bindings);
        return Expression.Lambda(typeof(Func<,>).MakeGenericType(entityType, typeof(AggregateRow)), body, p);
    }

    private static MethodCallExpression CountCall(ParameterExpression p, PropertyInfo prop, bool distinct) =>
        Expression.Call(Aggregate(distinct ? nameof(SqlFunc.AggregateDistinctCount) : nameof(SqlFunc.AggregateCount), prop.PropertyType),
            Expression.Property(p, prop));

    private static string AggregateMethodName(AggregateOp op) => op switch
    {
        AggregateOp.Count => nameof(SqlFunc.AggregateCount),
        AggregateOp.Sum => nameof(SqlFunc.AggregateSum),
        AggregateOp.Min => nameof(SqlFunc.AggregateMin),
        AggregateOp.Max => nameof(SqlFunc.AggregateMax),
        AggregateOp.Avg => nameof(SqlFunc.AggregateAvg),
        _ => throw new ArgumentOutOfRangeException(nameof(op)),
    };

    private static MethodInfo Aggregate(string name, Type argumentType) =>
        typeof(SqlFunc).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == name && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .MakeGenericMethod(argumentType);

    private static PropertyInfo Property(Type entityType, string clrProperty) =>
        entityType.GetProperty(clrProperty, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
        ?? throw new InvalidOperationException($"'{entityType.Name}' has no property '{clrProperty}'.");
}
