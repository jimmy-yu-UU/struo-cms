using Struo.Application.Search;

namespace Struo.Tests.Support;

/// <summary>Test double: records every <see cref="SearchRequest"/> and answers with the scripted outcome.</summary>
public sealed class ScriptedSearchProvider(Func<SearchRequest, SearchOutcome> script) : ISearchProvider
{
    public List<SearchRequest> Requests { get; } = [];

    public Task<SearchOutcome> SearchAsync(SearchRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        return Task.FromResult(script(request));
    }
}
