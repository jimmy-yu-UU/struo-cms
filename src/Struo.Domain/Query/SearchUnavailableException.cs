namespace Struo.Domain.Query;

/// <summary>
/// Thrown by an <c>ISearchProvider</c> implementation when its engine cannot answer (connection
/// refused, timeout, index missing). Mapped to <c>SEARCH_UNAVAILABLE</c> / 503 with a FIXED
/// client-facing message — this exception's own message is logged server-side and never sent to
/// the client, so it may safely name hosts or endpoints.
/// </summary>
public sealed class SearchUnavailableException : Exception
{
    public SearchUnavailableException(string message) : base(message) { }
    public SearchUnavailableException(string message, Exception innerException) : base(message, innerException) { }
}
