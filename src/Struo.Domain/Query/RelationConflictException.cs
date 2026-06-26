// src/Struo.Domain/Query/RelationConflictException.cs
namespace Struo.Domain.Query;

public sealed class RelationConflictException(string message) : Exception(message);
