// src/Struo.Domain/Query/ConflictException.cs
namespace Struo.Domain.Query;

/// <summary>A write collides with existing data (e.g. a duplicate unique value); surfaces as 409 CONFLICT.</summary>
public sealed class ConflictException(string message) : Exception(message);
