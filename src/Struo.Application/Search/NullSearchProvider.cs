namespace Struo.Application.Search;

/// <summary>Core's default: never handles a search, so the built-in LIKE path runs unchanged.</summary>
public sealed class NullSearchProvider : ISearchProvider
{
    public static NullSearchProvider Instance { get; } = new();

    public Task<SearchOutcome> SearchAsync(SearchRequest request, CancellationToken ct = default) =>
        Task.FromResult(SearchOutcome.NotHandled);
}
