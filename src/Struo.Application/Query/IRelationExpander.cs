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
    /// <param name="projectTarget"><c>(targetCollection, targetEntity, fields?) -&gt; camelCase dict</c>.</param>
    /// <param name="parentId">Reads the primary-key value off a parent entity.</param>
    /// <param name="readProp">Reads a property value off an entity by CLR/camel name.</param>
    Task<Dictionary<object, Dictionary<string, object?>>> ExpandAsync(
        string collection,
        IReadOnlyList<object> parents,
        DeepSpec deep,
        Func<string, object, IReadOnlyList<string>?, IReadOnlyDictionary<string, object?>> projectTarget,
        Func<object, object> parentId,
        Func<object, string, object?> readProp,
        CancellationToken ct = default);
}
