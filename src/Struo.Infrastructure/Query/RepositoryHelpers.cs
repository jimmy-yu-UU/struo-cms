// src/Struo.Infrastructure/Query/RepositoryHelpers.cs
using Struo.Application.Metadata;

namespace Struo.Infrastructure.Query;

internal static class RepositoryHelpers
{
    // Resolve the SqlSugar CSharpTypeName for a HAND-BUILT ConditionalModel so id/FK values bind as
    // their real CLR type (Guid -> uuid, long -> bigint) on Postgres instead of as text (42883 on PG).
    // Mirrors what ConditionalModelTranslator already does for the parsed query DSL; returns null for
    // string/unknown so those keep untyped behavior.
    public static string? TypeNameOf(object? sample) =>
        sample is null ? null : ConditionalModelTranslator.SqlSugarTypeName(sample.GetType());

    public static string? TypeNameOfProperty(Type entityType, string clrPropertyName)
    {
        var pt = entityType.GetProperty(clrPropertyName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.IgnoreCase)?.PropertyType;
        return pt is null ? null : ConditionalModelTranslator.SqlSugarTypeName(Nullable.GetUnderlyingType(pt) ?? pt);
    }

    public static EntityDescriptor Descriptor(IEntityRegistry registry, string collection) =>
        registry.Get(collection) ?? throw new InvalidOperationException($"Unknown collection '{collection}'.");

    /// <summary>
    /// Converts a string ID to the PK property type. Handles Guid and all IConvertible types.
    /// Delegates entirely to the Application-layer twin so any unparseable id surfaces as a
    /// mappable <see cref="Struo.Domain.Query.QueryException"/> (-&gt; HTTP 400) instead of a raw FormatException/
    /// ArgumentException that <c>StruoExceptionHandler.Map</c> cannot map and masks as a 500.
    /// </summary>
    public static object ConvertId(string id, EntityDescriptor d)
    {
        var idType = d.EntityType.GetProperty(d.IdProperty)!.PropertyType;
        return Struo.Application.Query.IdParsing.ParseTo(id, idType);
    }
}
