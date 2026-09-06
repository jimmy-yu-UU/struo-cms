// src/Struo.Infrastructure/Query/FacetQueries.Leaf.cs
using System.Reflection;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

internal sealed partial class FacetQueries
{
    private static readonly GenericDispatcher<Func<FacetQueries, object[], CancellationToken, Task<IReadOnlyList<object>>>> LoadByIdsDispatcher =
        new(typeof(FacetQueries), nameof(LoadByIds), [typeof(object[]), typeof(CancellationToken)]);

    private async Task<IReadOnlyList<object>> LoadByIds<T>(object[] ids, CancellationToken ct) where T : class, new() =>
        (await db.Queryable<T>().In(ids).ToListAsync(ct)).Cast<object>().ToList();

    private async Task<IReadOnlyList<FacetBucket>> SwapLeafValues(List<FacetBucket> idBuckets, ResolvedFacetPath facet, string? queryLocale, int maxValues, CancellationToken ct)
    {
        if (idBuckets.Count == 0) return idBuckets;
        var target = RepositoryHelpers.Descriptor(registry, facet.TargetCollection!);
        var ids = idBuckets.Select(b => b.Value!).ToArray();
        var leafByRawId = await LeafValuesAsync(target, facet, ids, queryLocale, ct);

        var merged = new Dictionary<LeafKey, long>();
        foreach (var b in idBuckets)
        {
            if (!leafByRawId.TryGetValue(b.Value!, out var leaf)) continue;
            var key = new LeafKey(leaf);
            merged[key] = merged.GetValueOrDefault(key) + b.Count;
        }
        return merged
            .Select(kv => new FacetBucket(kv.Key.Value, kv.Value))
            .OrderByDescending(b => b.Count)
            .ThenBy(b => b.Value, LeafValueComparer.Instance)
            .Take(maxValues)
            .ToList();
    }

    private async Task<Dictionary<object, object?>> LeafValuesAsync(EntityDescriptor target, ResolvedFacetPath facet, object[] ids, string? queryLocale, CancellationToken ct)
    {
        var tm = metadata.GetCollection(facet.TargetCollection!)?.Translation;
        var translatable = tm is not null && tm.Fields.Any(f => string.Equals(f, facet.LeafField, StringComparison.OrdinalIgnoreCase));
        var result = new Dictionary<object, object?>();
        if (translatable)
        {
            // TranslationStore.LoadTranslationsAsync only filters by locale when one is given; a null
            // queryLocale would return every locale's row for each id and let the last one win — silently
            // nondeterministic. A translatable leaf facet requires a locale (ItemService always supplies
            // one, falling back to the default language), so a caller that skips it fails loudly instead.
            if (queryLocale is null)
                throw new InvalidOperationException(
                    $"A query locale is required to facet on translatable field '{facet.LeafField}' of collection '{facet.TargetCollection}'.");

            // Missing-translation handling: LoadTranslationsAsync only returns rows that actually exist
            // at queryLocale, so any id left at its seeded `null` below has no translation row for that
            // locale and lands in the null bucket — as opposed to the non-translatable branch, where a
            // missing target row (soft-deleted) drops the id's bucket entirely instead.
            var rows = await translations.LoadTranslationsAsync(tm!.TranslationEntityType, tm.ForeignKeyProperty, tm.LocaleProperty, ids, queryLocale, ct);
            var fk = tm.TranslationEntityType.GetProperty(tm.ForeignKeyProperty)!;
            var leaf = tm.TranslationEntityType.GetProperty(facet.LeafField!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;
            foreach (var id in ids) result[id] = null;
            foreach (var r in rows) result[fk.GetValue(r)!] = leaf.GetValue(r);
            return result;
        }
        var entities = await LoadByIdsDispatcher.For(target.EntityType)(this, ids, ct);
        var idProp = target.Properties[target.IdProperty];
        var leafProp = target.Properties[target.FieldToProperty[facet.LeafField!]];
        foreach (var e in entities) result[idProp.GetValue(e)!] = leafProp.GetValue(e);
        return result;
    }

    private readonly record struct LeafKey(object? Value);

    private sealed class LeafValueComparer : IComparer<object?>
    {
        public static readonly LeafValueComparer Instance = new();
        public int Compare(object? x, object? y) => (x, y) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            (string a, string b) => string.CompareOrdinal(a, b),
            (IComparable a, _) when x.GetType() == y.GetType() => a.CompareTo(y),
            _ => string.CompareOrdinal(x.ToString(), y.ToString()),
        };
    }
}
