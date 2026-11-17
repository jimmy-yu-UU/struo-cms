using FluentMigrator;
using Struo.Application.Configuration;
using Struo.Application.Security;

namespace Struo.Infrastructure.Migrations.Core;

/// <summary>
/// On MySQL and MariaDB, re-declares every core string column with <c>AsString</c>, which the migration
/// type map renders as a utf8mb4 type, so a database that was migrated earlier ends in the same character
/// set as a new one. Each statement restates the column's length and nullability; none of these columns
/// has a default. The users and roles tables, whose unique values people type, come first.
/// It produces no expression on other backends. <see cref="Down"/> does nothing. Evidence:
/// docs/ai/decisions/mysql-string-columns-are-utf8mb4.md.
/// </summary>
[Migration(Version, "ConvertStringColumnsToUtf8Mb4")]
public sealed class ConvertStringColumnsToUtf8Mb4 : StruoMigration
{
    public const long Version = 202611170000;

    private const int Standard = 255;
    private const int VersionDescription = 1024;

    private sealed record Column(string Table, string Name, int Length, bool Nullable);

    private static readonly Column[] Columns =
    [
        new("users", "email", EmailAddress.MaxLength, false),
        new("users", "password", Standard, false),
        new("users", "name", Standard, true),
        new("users", "accesstoken", Standard, true),
        new("roles", "name", Standard, false),
        new("roles", "description", Standard, true),
        new("languages", "code", Standard, false),
        new("languages", "name", Standard, false),
        new("files", "storagekey", Standard, false),
        new("files", "filename", Standard, false),
        new("files", "contenttype", Standard, false),
        new("files", "status", Standard, false),
        new("file_translations", "locale", Standard, false),
        new("file_translations", "title", Standard, true),
        new("file_translations", "alt", Standard, true),
        new("media_folders", "name", Standard, false),
        new("permissions", "collection", Standard, false),
        new("revisions", "collectionname", Standard, false),
        new("revisions", "itemid", Standard, false),
        new("revisions", "operation", Standard, false),
        new("user_sessions", "ticketkey", Standard, false),
        new("schema_versions", "description", VersionDescription, true),
    ];

    public override void Up()
    {
        if (Db != StruoDbType.MySql) return;
        foreach (var column in Columns) Redeclare(column);
    }

    public override void Down()
    {
    }

    private void Redeclare(Column column)
    {
        var typed = Alter.Column(column.Name).OnTable(FrameworkTable(column.Table)).AsString(column.Length);
        if (column.Nullable) typed.Nullable();
        else typed.NotNullable();
    }
}
