namespace Struo.Infrastructure.Migrations.Schema;

/// <summary>An index as the database catalog reports it. Columns are lower-case, in key order.</summary>
public sealed record DbIndex(string Name, bool IsUnique, IReadOnlyList<string> Columns);
