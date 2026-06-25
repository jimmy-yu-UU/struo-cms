// src/Struo.Domain/Query/SortField.cs
namespace Struo.Domain.Query;

public sealed record SortField(string Field, bool Descending);
