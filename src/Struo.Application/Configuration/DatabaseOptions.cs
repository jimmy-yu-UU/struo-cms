using System.ComponentModel.DataAnnotations;

namespace Struo.Application.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public StruoDbType DbType { get; set; } = StruoDbType.PostgreSQL;

    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Prepended to the table name of every framework-owned entity (<c>FrameworkEntityTypes.All</c>
    /// and <c>SchemaMigration</c>); sample and fork entities are unaffected. Lower-case only: PostgreSQL
    /// folds unquoted identifiers and SQLite does not, and every table-name comparison in the
    /// infrastructure layer is case-insensitive. Sixteen characters keeps the longest core index
    /// name under PostgreSQL's 63-byte identifier limit. Empty string means no prefix.
    /// </summary>
    [RegularExpression("^(|[a-z][a-z0-9_]{0,15})$",
        ErrorMessage = "Database:TablePrefix must be empty or a lower-case letter followed by up to 15 lower-case letters, digits or underscores.")]
    public string TablePrefix { get; set; } = "struo_";

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
