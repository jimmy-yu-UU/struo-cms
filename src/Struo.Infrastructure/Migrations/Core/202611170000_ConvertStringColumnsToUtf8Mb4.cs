using FluentMigrator;
using Struo.Application.Configuration;
using Struo.Application.Security;

namespace Struo.Infrastructure.Migrations.Core;

/// <summary>
/// On MySQL and MariaDB, re-declares every core string column with <c>AsString</c>, which the migration
/// type map renders without a character set, so each column takes the default of its table. It runs only
/// where the database and the existing core tables default to utf8mb4, because <c>migrate</c> refuses any
/// other database before it applies a migration; each column then ends in utf8mb4, as in a new database.
/// Each statement restates the column's length and nullability; none of these columns has a default. The
/// users and roles tables, whose unique values people type, come first. It produces no expression on other
/// backends. <see cref="Down"/> does nothing. Evidence: docs/ai/decisions/mysql-string-columns-are-utf8mb4.md.
/// </summary>
[Migration(Version, "ConvertStringColumnsToUtf8Mb4")]
public sealed class ConvertStringColumnsToUtf8Mb4 : StruoMigration
{
    public const long Version = 202611170000;

    private const int Standard = 255;
    private const int VersionDescription = 1024;

    private sealed record Column(string Name, int Length, bool Nullable);

    private sealed record TableColumns(string Table, Column[] Columns);

    private static Column Required(string name, int length = Standard) => new(name, length, false);

    private static Column Optional(string name, int length = Standard) => new(name, length, true);

    private static readonly TableColumns[] Tables =
    [
        new("users", [Required("email", EmailAddress.MaxLength), Required("password"), Optional("name"), Optional("accesstoken")]),
        new("roles", [Required("name"), Optional("description")]),
        new("languages", [Required("code"), Required("name")]),
        new("files", [Required("storagekey"), Required("filename"), Required("contenttype"), Required("status")]),
        new("file_translations", [Required("locale"), Optional("title"), Optional("alt")]),
        new("media_folders", [Required("name")]),
        new("permissions", [Required("collection")]),
        new("revisions", [Required("collectionname"), Required("itemid"), Required("operation")]),
        new("user_sessions", [Required("ticketkey")]),
        new("schema_versions", [Optional("description", VersionDescription)]),
    ];

    public override void Up()
    {
        if (Db != StruoDbType.MySql) return;
        foreach (var table in Tables)
            foreach (var column in table.Columns)
                Redeclare(table.Table, column);
    }

    public override void Down()
    {
    }

    private void Redeclare(string table, Column column)
    {
        var typed = Alter.Column(column.Name).OnTable(FrameworkTable(table)).AsString(column.Length);
        if (column.Nullable) typed.Nullable();
        else typed.NotNullable();
    }
}
