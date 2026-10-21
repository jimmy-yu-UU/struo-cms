using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// The arguments of <see cref="IItemRepository.QueryWhereInWindowAsync"/>: which relation rows to read
/// (<paramref name="Collection"/>, <paramref name="Property"/> in <paramref name="Values"/>, plus an
/// optional <paramref name="ExtraFilter"/>) and the window over them (<paramref name="Sort"/>,
/// <paramref name="Offset"/>, <paramref name="Limit"/>). <paramref name="QueryLocale"/> resolves a
/// translatable field in the filter or sort.
/// </summary>
public sealed record WhereInWindowRequest(
    string Collection,
    string Property,
    IReadOnlyList<object> Values,
    FilterNode? ExtraFilter,
    IReadOnlyList<SortField> Sort,
    int Offset,
    int Limit,
    string? QueryLocale);
