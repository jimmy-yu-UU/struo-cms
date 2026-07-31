using System.ComponentModel.DataAnnotations;

namespace Struo.Application.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public StruoDbType DbType { get; set; } = StruoDbType.PostgreSQL;

    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Directory of reviewed <c>*.sql</c> migration scripts to apply at startup via
    /// <c>MigrationRunner</c>. Null/empty (the default) disables the runner. Applies on <b>any</b>
    /// configured backend — the scripts you place there are yours, and are as portable as you write
    /// them. See <c>db/migrations/README.md</c>.
    /// </summary>
    public string? MigrationsPath { get; set; }

    /// <summary>
    /// Allows SqlSugar CodeFirst to structurally sync <b>existing</b> tables (add / modify /
    /// <b>drop</b> columns) against the entity classes. <b>Honoured in Development only</b>; setting it
    /// in any other environment is ignored with a warning. Default <c>false</c>.
    ///
    /// <para>
    /// Creating tables that do not yet exist is unconditional and unaffected by this flag.
    /// </para>
    /// </summary>
    public bool AutoSyncSchema { get; set; } = false;
}
