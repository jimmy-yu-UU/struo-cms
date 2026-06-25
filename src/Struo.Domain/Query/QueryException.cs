// src/Struo.Domain/Query/QueryException.cs
namespace Struo.Domain.Query;

/// <summary>Invalid query/parse input. Surfaced as HTTP 400.</summary>
public sealed class QueryException : Exception
{
    public QueryException(string message) : base(message) { }
}

/// <summary>Unknown collection name. Surfaced as HTTP 404.</summary>
public sealed class CollectionNotFoundException(string collection)
    : Exception($"Unknown collection '{collection}'.")
{
    public string Collection { get; } = collection;
}
