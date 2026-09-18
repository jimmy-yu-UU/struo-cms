// src/Struo.Infrastructure/Query/FacetQueries.Leaf.cs
using System.Reflection;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Auditing;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

internal sealed partial class FacetQueries
{
    private static readonly GenericDispatcher<Func<FacetQueries, object[], CancellationToken, Task<IReadOnlyList<object>>>> LoadByIdsDispatcher =
        new(typeof(FacetQueries), nameof(LoadByIds), [typeof(object[]), typeof(CancellationToken)]);

    private async Task<IReadOnlyList<object>> LoadByIds<T>(object[] ids, CancellationToken ct) where T : class, new() =>
        (await db.Queryable<T>().In(ids).ToListAsync(ct)).Cast<object>().ToList();

    // The extra typed `In(ids)` cost this drop adds is paid in exactly two places: once here, for a
    // many-to-one/many-to-many id-bucket facet on a soft-deletable target (FacetQueries.FacetAsync's
    // Relation case, one-to-many skipped there since its own side query already carries the global
    // filter), and once more inside LeafValuesAsync's translatable branch below, for the equivalent
    // leaf form. Everywhere else — own-field, FK, one-to-many id buckets, and the non-translatable
    // leaf branch (already going through LoadByIds<T> for its own reasons) — nothing extra runs.
    private async Task<object[]> LiveIds(EntityDescriptor target, object[] ids, CancellationToken ct)
    {
        if (ids.Length == 0 || !typeof(ISoftDeletable).IsAssignableFrom(target.EntityType))
            return ids;

        var live = await LoadByIdsDispatcher.For(target.EntityType)(this, ids, ct);
        var idProp = target.Properties[target.IdProperty];
        return live.Select(e => idProp.GetValue(e)!).ToArray();
    }

    // A many-to-many/many-to-one relation facet's id bucket comes from the junction row or the
    // root's own FK column — both survive a target row's soft-delete — so a trashed target would
    // otherwise still contribute an id bucket. Drop any bucket whose target row is soft-deleted.
    private async Task<List<FacetBucket>> DropSoftDeletedTargets(List<FacetBucket> idBuckets, EntityDescriptor target, CancellationToken ct)
    {
        if (idBuckets.Count == 0) return idBuckets;

        var ids = idBuckets.Select(b => b.Value!).ToArray();
        var liveIds = (await LiveIds(target, ids, ct)).ToHashSet();
        return idBuckets.Where(b => liveIds.Contains(b.Value!)).ToList();
    }

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

            // The translation sidecar is keyed by the target's id and has no soft-delete concept of
            // its own — a trashed target's translation row (if any) would otherwise still surface
            // through the lookup below. Drop a soft-deleted id BEFORE the sidecar read (rather than
            // filtering the returned rows after) so it never gets seeded into `result` at all — SwapLeafValues
            // above then treats it exactly like the non-translatable branch's missing dictionary
            // entry: skipped, not merged into a null bucket.
            var liveIds = await LiveIds(target, ids, ct);

            // Missing-translation handling: LoadTranslationsAsync only returns rows that actually exist
            // at queryLocale, so any id left at its seeded `null` below has no translation row for that
            // locale and lands in the null bucket — as opposed to the non-translatable branch, where a
            // missing target row (hard-deleted / dangling junction FK) drops the id's bucket entirely instead.
            var rows = await translations.LoadTranslationsAsync(tm!.TranslationEntityType, tm.ForeignKeyProperty, tm.LocaleProperty, liveIds, queryLocale, ct);
            var fk = tm.TranslationEntityType.GetProperty(tm.ForeignKeyProperty)!;
            var leaf = tm.TranslationEntityType.GetProperty(facet.LeafField!, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)!;
            foreach (var id in liveIds) result[id] = null;
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
