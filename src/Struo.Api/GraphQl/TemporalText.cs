// src/Struo.Api/GraphQl/TemporalText.cs
using System.Globalization;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// The text of a <c>Date</c>, <c>DateTime</c> or <c>Time</c> field. The property holds a
/// <see cref="DateTime"/>, which in this application is a UTC instant. A <c>Date</c> prints as its date
/// (<c>yyyy-MM-dd</c>); a <c>DateTime</c> and a <c>Time</c> print as that instant in UTC ending in <c>Z</c>,
/// with fractional seconds only when there are some, the form the GraphQL DateTime scalar prints. The field's
/// interface decides the form, so the same value reads the same in an own field and in an <c>Any</c> value
/// (a translation's fields, a facet value, an aggregate value).
/// </summary>
internal static class TemporalText
{
    private static readonly string[] AggregateValueOps = ["min", "max"];

    internal static bool IsTemporal(FieldInterface face) =>
        face is FieldInterface.Date or FieldInterface.DateTime or FieldInterface.Time;

    // A String field that holds a DateTime is a Time field.
    internal static object? Instant(object? value) => value is DateTime dateTime ? FormatInstant(dateTime) : value;

    internal static object? Of(FieldInterface face, object? value)
    {
        if (value is not DateTime dateTime) return value;
        return face == FieldInterface.Date ? FormatDate(dateTime) : FormatInstant(dateTime);
    }

    internal static string FormatDate(DateTime value) => value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    internal static string FormatInstant(DateTime value)
    {
        var utc = value.Kind == DateTimeKind.Local ? value.ToUniversalTime() : value;
        return utc.ToString("yyyy-MM-dd'T'HH':'mm':'ss.FFFFFFF'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>The temporal fields among <paramref name="fields"/>, by name.</summary>
    internal static IReadOnlyDictionary<string, FieldInterface> Kinds(IEnumerable<FieldMetadata> fields, StringComparer names) =>
        fields.Where(f => IsTemporal(f.Interface)).ToDictionary(f => f.Name, f => f.Interface, names);

    /// <summary>The fields of one translation: a temporal field is printed by its interface.</summary>
    internal static object? InFields(object? fields, IReadOnlyDictionary<string, FieldInterface> kinds) =>
        kinds.Count > 0 && fields is IDictionary<string, object?> dict
            ? dict.ToDictionary(kv => kv.Key, kv => ValueOf(kinds, kv.Key, kv.Value))
            : fields;

    /// <summary>
    /// The buckets of the facets whose path ends in a temporal field: an own field, or a field of a
    /// related collection one hop away.
    /// </summary>
    internal static IReadOnlyList<FacetResult> InFacets(
        IReadOnlyList<FacetResult> facets, CollectionMetadata meta, IMetadataProvider metadata) =>
        facets.Select(f => PathInterface(meta, f.Field, metadata) is { } face && IsTemporal(face)
            ? f with { Values = f.Values.Select(b => b with { Value = Of(face, b.Value) }).ToList() }
            : f).ToList();

    /// <summary>The values of <c>min</c> and <c>max</c> over a temporal field; every other value is unchanged.</summary>
    internal static IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? InAggregate(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? aggregate,
        IReadOnlyDictionary<string, FieldInterface> kinds) =>
        aggregate?.ToDictionary(op => op.Key, op => ValuesOf(op.Key, op.Value, kinds), StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, object?> ValuesOf(
        string op, IReadOnlyDictionary<string, object?> values, IReadOnlyDictionary<string, FieldInterface> kinds)
    {
        if (!AggregateValueOps.Contains(op)) return values;
        return values.ToDictionary(kv => kv.Key, kv => ValueOf(kinds, kv.Key, kv.Value));
    }

    private static object? ValueOf(IReadOnlyDictionary<string, FieldInterface> kinds, string field, object? value) =>
        kinds.TryGetValue(field, out var face) ? Of(face, value) : value;

    // A facet path is an own field or `<relation>.<field>`; the names match case-insensitively.
    private static FieldInterface? PathInterface(CollectionMetadata meta, string path, IMetadataProvider metadata)
    {
        var parts = path.Split('.');
        var owner = meta;
        if (parts.Length == 2)
        {
            var relation = meta.Relations.FirstOrDefault(r => string.Equals(r.Name, parts[0], StringComparison.OrdinalIgnoreCase));
            owner = relation is null ? null : metadata.GetCollection(relation.TargetCollection);
        }
        return owner?.Fields.FirstOrDefault(f => string.Equals(f.Name, parts[^1], StringComparison.OrdinalIgnoreCase))?.Interface;
    }
}
