// src/Struo.Domain/Query/ConcurrencyConflictException.cs
namespace Struo.Domain.Query;

/// <summary>
/// Thrown when an optimistic-concurrency guard fails: the row was modified by someone else since the
/// caller read it (the update matched zero rows on <c>WHERE version = expected</c>). The API maps this
/// to HTTP 409 Conflict. Web-free by design (§2).
/// </summary>
public sealed class ConcurrencyConflictException(string message) : Exception(message);
