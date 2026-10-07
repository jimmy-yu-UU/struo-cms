using System.ComponentModel.DataAnnotations;

namespace Struo.Application.Configuration;

public sealed class DatabaseOptions
{
    public const string SectionName = "Database";
    public StruoDbType DbType { get; set; } = StruoDbType.PostgreSQL;

    [Required(AllowEmptyStrings = false)]
    public string ConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Prepended to the table name of every framework-owned entity (<c>FrameworkEntityTypes.All</c>) and
    /// the migration version table; sample and fork entities are unaffected. Lower-case only: PostgreSQL
    /// folds unquoted identifiers and SQLite does not, and every table-name comparison in the
    /// infrastructure layer is case-insensitive. Sixteen characters keeps the longest core index
    /// name under PostgreSQL's 63-byte identifier limit. Empty string means no prefix.
    /// </summary>
    [RegularExpression("^(|[a-z][a-z0-9_]{0,15})$",
        ErrorMessage = "Database:TablePrefix must be empty or a lower-case letter followed by up to 15 lower-case letters, digits or underscores.")]
    public string TablePrefix { get; set; } = "struo_";

    /// <summary>
    /// Seconds the <c>migrate</c> command waits for the migration lock before failing. Range 1-3600.
    /// </summary>
    [Range(1, 3600)]
    public int MigrationLockTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Applies pending migrations at startup, under the migration lock, before the host starts serving.
    /// Default <c>false</c>: the host then refuses to start until the <c>migrate</c> command has run.
    /// </summary>
    public bool MigrateOnStartup { get; set; } = false;
}
