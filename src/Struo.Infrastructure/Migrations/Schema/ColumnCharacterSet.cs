namespace Struo.Infrastructure.Migrations.Schema;

/// <summary>The character set a MySQL or MariaDB column reports; null for a column that is not text.</summary>
internal sealed record ColumnCharacterSet(string Column, string? CharacterSet);
