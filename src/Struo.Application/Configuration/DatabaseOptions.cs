namespace Struo.Application.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public StruoDbType DbType { get; set; } = StruoDbType.PostgreSQL;
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Directory of reviewed <c>*.sql</c> migration scripts to apply at startup via
    /// <c>MigrationRunner</c>. Null/empty (the default) disables the runner. Only honoured when
    /// <see cref="DbType"/> is PostgreSQL; on any other backend the runner is a no-op.
    /// </summary>
    public string? MigrationsPath { get; set; }
}
