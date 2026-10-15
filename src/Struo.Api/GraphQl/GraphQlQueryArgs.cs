// src/Struo.Api/GraphQl/GraphQlQueryArgs.cs
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>The raw list-query arguments a GraphQL resolver hands to <see cref="GraphQlQueryBuilder.BuildQuery"/>.</summary>
public sealed record GraphQlQueryArgs(
    IReadOnlyDictionary<string, object?>? Filter,
    IReadOnlyList<string>? Sort,
    int? Limit,
    int? Offset,
    string? Search,
    DeepSpec? Deep,
    IReadOnlyList<string>? Facets = null,
    IReadOnlyDictionary<string, object?>? Aggregate = null);
