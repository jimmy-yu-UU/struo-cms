// src/Struo.Domain/Query/QueryException.cs
namespace Struo.Domain.Query;

/// <summary>One field-level problem behind a <see cref="QueryException"/>; <c>Locale</c> is set for a per-locale translation field.</summary>
public sealed record QueryErrorDetail(string Field, string? Locale, string Message);

/// <summary>Invalid query/parse input. Surfaced as HTTP 400.</summary>
public sealed class QueryException : Exception
{
    public QueryException(string message) : base(message) { }

    public QueryException(string message, IReadOnlyList<QueryErrorDetail> details) : base(message)
    {
        Details = details;
    }

    /// <summary>The field-level problems, or null when the error carries none.</summary>
    public IReadOnlyList<QueryErrorDetail>? Details { get; }
}

/// <summary>Unknown collection name. Surfaced as HTTP 404.</summary>
public sealed class CollectionNotFoundException(string collection)
    : Exception($"Unknown collection '{collection}'.")
{
    public string Collection { get; } = collection;
}
