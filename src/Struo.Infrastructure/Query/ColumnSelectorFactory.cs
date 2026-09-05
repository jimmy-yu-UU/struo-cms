// src/Struo.Infrastructure/Query/ColumnSelectorFactory.cs
using System.Linq.Expressions;
using System.Reflection;

namespace Struo.Infrastructure.Query;

internal static class ColumnSelectorFactory
{
    // x => (object)x.Prop — the shape ISugarQueryable<T>.In(Expression<Func<T,object>>, ISugarQueryable<TField>) needs.
    public static LambdaExpression ObjectSelector(Type entityType, string clrProperty)
    {
        var p = Expression.Parameter(entityType, "x");
        Expression body = Expression.Property(p, Property(entityType, clrProperty));
        if (body.Type.IsValueType) body = Expression.Convert(body, typeof(object));
        return Expression.Lambda(typeof(Func<,>).MakeGenericType(entityType, typeof(object)), body, p);
    }

    // x => x.Prop with the property's REAL CLR type — SqlSugar's Select() only turns this shape into a
    // one-column projection (a boxed Convert body yields an empty/garbage result).
    public static LambdaExpression TypedSelector(Type entityType, string clrProperty)
    {
        var p = Expression.Parameter(entityType, "x");
        var prop = Property(entityType, clrProperty);
        return Expression.Lambda(typeof(Func<,>).MakeGenericType(entityType, prop.PropertyType), Expression.Property(p, prop), p);
    }

    public static Type PropertyType(Type entityType, string clrProperty) => Property(entityType, clrProperty).PropertyType;

    private static PropertyInfo Property(Type entityType, string clrProperty) =>
        entityType.GetProperty(clrProperty, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
        ?? throw new InvalidOperationException($"'{entityType.Name}' has no property '{clrProperty}'.");
}
