// src/Struo.Domain/Query/DeepSpec.cs
namespace Struo.Domain.Query;

/// <summary>
/// Per-relation deep-expansion options: an optional field whitelist for the expanded target
/// rows, an optional limit (reserved — consumed by 8c.3b nested-list args), and an optional
/// nested <see cref="DeepSpec"/> for multi-level (depth &gt; 1) expansion of the target's own
/// relations. <c>Deep</c> defaults to null so depth-1 call sites are unchanged.
/// </summary>
public sealed record DeepRelationSpec(
    IReadOnlyList<string>? Fields, int? Limit, DeepSpec? Deep = null);

/// <summary>
/// Read-time relation-expansion request: maps each requested relation name to its
/// <see cref="DeepRelationSpec"/>. Lookups are case-insensitive. Recursion rides on the
/// per-relation <see cref="DeepRelationSpec.Deep"/>.
/// </summary>
public sealed record DeepSpec(IReadOnlyDictionary<string, DeepRelationSpec> Relations);
