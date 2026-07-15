// src/Struo.Application/Query/PropertyAccessorCache.cs
using System.Collections.Concurrent;
using System.Reflection;

namespace Struo.Application.Query;

/// <summary>
/// Process-wide cache of <c>(declaring type, property name) -&gt; PropertyInfo</c> for the projection
/// and revision-snapshot read paths (CS-4/ARC-2). It replaces per-row <c>Type.GetProperty</c>
/// reflection on values that do not have an <see cref="Metadata.EntityDescriptor"/> in hand —
/// relation targets, repeater child POCOs, M2M junction rows and translation rows.
/// <para>
/// Lookup semantics are byte-for-byte identical to the inline reflection they replace: resolution
/// uses <c>Public | Instance | IgnoreCase</c> binding, and a missing property yields <c>null</c>
/// (preserving the old <c>?.</c> null-flow). The key compares its string component ordinally, so two
/// different spellings of the same property (e.g. "authorId" / "AuthorId") occupy two entries that
/// both resolve to the same accessor — never a wrong result, at worst a spare entry. Call sites pass
/// a stable spelling, so that is negligible and avoids a bespoke tuple comparer (YAGNI).
/// </para>
/// </summary>
internal static class PropertyAccessorCache
{
    private static readonly ConcurrentDictionary<(Type Type, string Name), PropertyInfo?> Cache = new();

    public static PropertyInfo? Resolve(Type type, string propertyName) =>
        Cache.GetOrAdd((type, propertyName), static key =>
            key.Type.GetProperty(
                key.Name,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase));

    public static object? Read(object instance, string propertyName) =>
        Resolve(instance.GetType(), propertyName)?.GetValue(instance);
}
