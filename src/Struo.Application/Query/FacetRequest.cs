// src/Struo.Application/Query/FacetRequest.cs
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Bundles <see cref="IItemRepository.FacetAsync"/>'s per-call inputs into one parameter object
/// (Sonar S107: a method must not take more than 7 parameters) — see the members' doc comments on
/// <see cref="IItemRepository.FacetAsync"/> for what each one means.
/// </summary>
public sealed record FacetRequest(
    string Collection,
    QueryModel PrunedQuery,
    ResolvedFacetPath Facet,
    IReadOnlyList<string> SearchableFields,
    string? QueryLocale,
    DeletedFilter Deleted,
    int MaxValues);
