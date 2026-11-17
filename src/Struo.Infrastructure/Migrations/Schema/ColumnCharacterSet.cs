namespace Struo.Infrastructure.Migrations.Schema;

/// <summary>The character set and collation a MySQL or MariaDB column reports; null for a column that is not text.</summary>
internal sealed record ColumnCharacterSet(string Column, string? CharacterSet, string? Collation);
