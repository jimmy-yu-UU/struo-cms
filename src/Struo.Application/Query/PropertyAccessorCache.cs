// src/Struo.Application/Query/PropertyAccessorCache.cs
using System.Collections.Concurrent;
using System.Reflection;

namespace Struo.Application.Query;

/// <summary>
/// Process-wide cache of <c>(declaring type, property name) -&gt; PropertyInfo</c>, used for values
/// that do not have an <see cref="Metadata.EntityDescriptor"/> in hand — relation targets, repeater
/// child POCOs, M2M junction rows and translation rows.
/// <para>
/// Resolution uses <c>Public | Instance | IgnoreCase</c> binding; a missing property yields
/// <c>null</c>, and <see cref="Read"/>'s <c>?.</c> turns that into a null value instead of a throw.
/// The key compares its string component ordinally, so two different spellings of the same property
/// (e.g. "authorId" / "AuthorId") occupy two entries that both resolve to the same accessor — never a
/// wrong result, at worst a spare entry. Call sites pass a stable spelling, so that is negligible and
/// avoids a bespoke tuple comparer (YAGNI).
/// </para>
/// <para>
/// The cache is process-wide and unbounded: every distinct <c>(type, name)</c> key ever requested stays
/// cached for the process's lifetime, so keys must come from metadata (entity descriptors, relation
/// metadata), never from caller-supplied strings, which could grow the cache without bound.
/// </para>
/// </summary>
public static class PropertyAccessorCache
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
