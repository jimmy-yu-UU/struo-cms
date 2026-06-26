// src/Struo.Domain/Query/DeepSpec.cs
namespace Struo.Domain.Query;

/// <summary>
/// Per-relation deep-expansion options: an optional field whitelist for the
/// expanded target rows and an optional limit (limit reserved for future use).
/// </summary>
public sealed record DeepRelationSpec(IReadOnlyList<string>? Fields, int? Limit);

/// <summary>
/// Read-time relation-expansion request: maps each requested relation name to
/// its <see cref="DeepRelationSpec"/>. Lookups are case-insensitive.
/// </summary>
public sealed record DeepSpec(IReadOnlyDictionary<string, DeepRelationSpec> Relations);
