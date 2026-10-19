// src/Struo.Infrastructure/Query/TypedPredicates.cs
using System.Linq.Expressions;

namespace Struo.Infrastructure.Query;

/// <summary>
/// Builds the typed <c>Where</c> lambdas for entities whose property names are only known at runtime.
/// SqlSugar renders the member access as a quoted column and the value as a bound parameter, so no SQL
/// text is assembled by hand.
/// </summary>
internal static class TypedPredicates
{
    // A value read through a field on a constant holder is bound as a parameter, not inlined.
    private sealed class ValueBox<TValue>(TValue value)
    {
        public readonly TValue BoundValue = value;
    }

    /// <summary>it =&gt; it.Property == value</summary>
    public static Expression<Func<T, bool>> Equal<T>(string property, object value)
    {
        var it = Expression.Parameter(typeof(T), "it");
        return Expression.Lambda<Func<T, bool>>(EqualBody(it, property, value), it);
    }

    /// <summary>it =&gt; it.Property == value &amp;&amp; it.Nullable == null (or != null)</summary>
    public static Expression<Func<T, bool>> EqualAndNullState<T>(
        string property, object value, string nullableProperty, bool mustBeNull)
    {
        var it = Expression.Parameter(typeof(T), "it");
        var member = Expression.Property(it, nullableProperty);
        var nullConstant = Expression.Constant(null, member.Type);
        Expression state = mustBeNull ? Expression.Equal(member, nullConstant) : Expression.NotEqual(member, nullConstant);
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(EqualBody(it, property, value), state), it);
    }

    /// <summary>it =&gt; it.Property == value &amp;&amp; it.Other == otherValue</summary>
    public static Expression<Func<T, bool>> EqualAndEqual<T>(
        string property, object value, string otherProperty, object otherValue)
    {
        var it = Expression.Parameter(typeof(T), "it");
        return Expression.Lambda<Func<T, bool>>(
            Expression.AndAlso(EqualBody(it, property, value), EqualBody(it, otherProperty, otherValue)), it);
    }

    private static BinaryExpression EqualBody(ParameterExpression it, string property, object value)
    {
        var member = Expression.Property(it, property);
        var propertyType = member.Type;
        var valueType = Nullable.GetUnderlyingType(propertyType) ?? propertyType;
        var box = Activator.CreateInstance(
            typeof(ValueBox<>).MakeGenericType(valueType), Convert.ChangeType(value, valueType))!;
        Expression bound = Expression.Field(Expression.Constant(box), nameof(ValueBox<int>.BoundValue));
        if (bound.Type != propertyType) bound = Expression.Convert(bound, propertyType);
        return Expression.Equal(member, bound);
    }
}
