// src/Struo.Application/Query/IRelationExpander.cs
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Expands <c>deep</c> relations for a page of parent entities via batched follow-up
/// queries. Implemented in Infrastructure (where the relationship-graph descriptors and
/// junction CLR types live); consumed by <see cref="ItemService"/> so the Application
/// layer never references SqlSugar internals.
/// </summary>
public interface IRelationExpander
{
    /// <summary>
    /// Returns, per parent primary-key value, a map of <c>relationName -&gt; (object?|list)</c>
    /// of projected target rows for every relation named in <paramref name="deep"/>.
    /// </summary>
    /// <param name="accessors">Reads and projects entities; see <see cref="EntityAccessors"/>.</param>
    /// <param name="canReadJunction">
    /// <c>junctionCollection -&gt; bool</c>, consulted before a payload-bearing M2M relation's
    /// <c>_junction</c> sub-object is attached, at every nesting level (this same delegate is
    /// threaded through the recursive call for a relation's own nested <c>deep</c>). <c>null</c>
    /// means "no restriction" — every payload-bearing M2M relation gets its <c>_junction</c>.
    /// </param>
    Task<Dictionary<object, Dictionary<string, object?>>> ExpandAsync(
        string collection,
        IReadOnlyList<object> parents,
        DeepSpec deep,
        EntityAccessors accessors,
        string? locale = null,
        Func<string, bool>? canReadJunction = null,
        CancellationToken ct = default);
}

/// <summary>The entity-reading and target-projection delegates an expansion works through.</summary>
/// <param name="ProjectTarget"><c>(targetCollection, targetEntity, fields?) -&gt; camelCase dict</c>.</param>
/// <param name="ParentId">Reads the primary-key value off a parent entity.</param>
/// <param name="ReadProp">Reads a property value off an entity by CLR/camel name.</param>
public sealed record EntityAccessors(
    Func<string, object, IReadOnlyList<string>?, IReadOnlyDictionary<string, object?>> ProjectTarget,
    Func<object, object> ParentId,
    Func<object, string, object?> ReadProp);
