// src/Struo.Domain/Query/DeepSpec.cs
namespace Struo.Domain.Query;

/// <summary>
/// Per-relation deep-expansion options: an optional field whitelist for the expanded target rows,
/// per-parent nested-list <see cref="Filter"/> (cross-relation, resolved against the target
/// collection), own-field <see cref="Sort"/>, per-parent <see cref="Limit"/>/<see cref="Offset"/>,
/// and an optional nested <see cref="DeepSpec"/> for multi-level (depth &gt; 1) expansion.
/// Filter/Sort/Limit/Offset apply to to-many (O2M/M2M) list relations only; they are null for M2O.
/// The positional parameters are unchanged from 8c.3a; Filter/Sort/Offset are additive init-only
/// properties so every existing construction site compiles unchanged.
/// </summary>
public sealed record DeepRelationSpec(
    IReadOnlyList<string>? Fields, int? Limit, DeepSpec? Deep = null)
{
    public FilterNode? Filter { get; init; }
    public IReadOnlyList<SortField>? Sort { get; init; }
    public int? Offset { get; init; }
}

/// <summary>
/// Read-time relation-expansion request: maps each requested relation name to its
/// <see cref="DeepRelationSpec"/>. Lookups are case-insensitive. Recursion rides on the
/// per-relation <see cref="DeepRelationSpec.Deep"/>.
/// </summary>
public sealed record DeepSpec(IReadOnlyDictionary<string, DeepRelationSpec> Relations);
