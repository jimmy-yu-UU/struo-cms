// src/Struo.Infrastructure/Query/ConditionalModelTranslator.cs
using System.Reflection;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Query;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Query;

/// <summary>
/// Turns one <see cref="ComparisonFilter"/> leaf over an own column into SqlSugar conditionals.
/// <see cref="FilterTranslator"/> is the only entry to a whole filter tree; it calls this for every
/// own-collection leaf, including leaves on a synthetic descriptor for a junction or translation entity.
/// </summary>
public static class ConditionalModelTranslator
{
    /// <summary>
    /// The result is one <see cref="ConditionalModel"/> or, for a set operator on a text column, a group;
    /// it is null when the operator adds no condition (an empty <c>_nin</c>).
    /// </summary>
    public static IConditionalModel? ToCondition(ComparisonFilter c, EntityDescriptor d, ISqlSugarClient db) =>
        ValueConditions.Build(Target(c.FieldPath, d, db), c.Op, c.Value);

    /// <summary>The terms a leaf contributes inside an <c>_or</c> group.</summary>
    internal static OrTerms ToOrTerms(ComparisonFilter c, EntityDescriptor d, ISqlSugarClient db) =>
        ValueConditions.OrMembers(Target(c.FieldPath, d, db), c.Op, c.Value);

    private static ValueTarget Target(string field, EntityDescriptor d, ISqlSugarClient db)
    {
        var property = d.EntityType
            .GetProperty(d.FieldToProperty.GetValueOrDefault(field) ?? string.Empty,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        var utcInstant = property is not null && UtcInstantConverter.IsUtcInstantProperty(property);
        return new ValueTarget(
            Column(d, db, field), field, property?.PropertyType ?? typeof(string),
            db.CurrentConnectionConfig.DbType, utcInstant);
    }

    private static string Column(EntityDescriptor d, ISqlSugarClient db, string field)
    {
        if (!d.FieldToProperty.TryGetValue(field, out var property))
            throw new ArgumentException(
                $"Unknown field path '{field}' for entity '{d.EntityType.Name}'.", nameof(field));

        return db.EntityMaintenance.GetDbColumnName(property, d.EntityType);
    }
}
