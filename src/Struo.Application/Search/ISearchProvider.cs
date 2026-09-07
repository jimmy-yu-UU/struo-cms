namespace Struo.Application.Search;

/// <summary>
/// The search seam a fork may implement to answer a list request's <c>search=</c> term with its own
/// engine (Meilisearch, Elasticsearch, PostgreSQL full-text, …). Core consults the registered
/// provider once per list request whenever the term is non-blank; <see cref="SearchOutcome.NotHandled"/>
/// keeps the built-in LIKE search, <see cref="SearchOutcome.Candidates"/> replaces it with
/// <c>id IN (candidates)</c>. Permissions, soft-delete, <c>Hidden</c>, filters, facets, aggregates,
/// sorting and paging all stay in core. Throw <see cref="Struo.Domain.Query.SearchUnavailableException"/>
/// to surface <c>SEARCH_UNAVAILABLE</c>/503, or catch and return NotHandled to degrade to LIKE.
/// The default registration is <see cref="NullSearchProvider"/>.
/// </summary>
public interface ISearchProvider
{
    Task<SearchOutcome> SearchAsync(SearchRequest request, CancellationToken ct = default);
}
