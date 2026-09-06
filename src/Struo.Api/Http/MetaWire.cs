using Struo.Domain.Query;

namespace Struo.Api.Http;

/// <summary>
/// Maps the application-layer facet/aggregate results onto the REST wire shape (spec §3.5):
/// <c>meta.facets["&lt;raw path&gt;"] = [{"value":…,"count":…}, …]</c> (facet key order = request
/// order) and <c>meta.aggregate = {"&lt;op&gt;":{"&lt;field&gt;":…}}</c> (op keys lower-cased).
/// Shared with the GraphQL layer's equivalent field so both surfaces render the same shape.
/// </summary>
internal static class MetaWire
{
    public static IReadOnlyDictionary<string, IReadOnlyList<FacetBucket>>? Facets(IReadOnlyList<FacetResult>? facets)
    {
        if (facets is null) return null;
        var wire = new Dictionary<string, IReadOnlyList<FacetBucket>>(StringComparer.Ordinal);
        foreach (var f in facets) wire[f.Field] = f.Values;
        return wire;
    }

    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>? Aggregate(AggregateResult? aggregate) =>
        aggregate?.Values.ToDictionary(kv => kv.Key.ToString().ToLowerInvariant(), kv => kv.Value, StringComparer.Ordinal);
}
