using System.Data;
using FluentMigrator.Runner.Generators.MySql;

namespace Struo.Infrastructure.Migrations;

/// <summary>
/// The MySQL and MariaDB type map of the migration runner. The framework map renders <c>AsString</c> and
/// <c>AsFixedLengthString</c> as national types, which both servers store as utf8mb3; this map renders them
/// without a character set, so a column takes the database's utf8mb4 default. Every key of the two text
/// types is restated, because a key that is not restated keeps the framework template. Evidence:
/// docs/ai/decisions/mysql-string-columns-are-utf8mb4.md.
/// </summary>
internal sealed class Utf8Mb4MySqlTypeMap : MySql8TypeMap
{
    private const int VarcharLimit = 8192;
    private const int CharLimit = 255;
    private const int TextLimit = 65535;
    private const int MediumTextLimit = 16777215;

    protected override void SetupMySqlTypeMaps()
    {
        base.SetupMySqlTypeMaps();

        SetTypeMap(DbType.String, "VARCHAR(255)");
        SetTypeMap(DbType.String, "VARCHAR($size)", VarcharLimit);
        SetTypeMap(DbType.String, "TEXT", TextLimit);
        SetTypeMap(DbType.String, "MEDIUMTEXT", MediumTextLimit);
        SetTypeMap(DbType.String, "LONGTEXT", int.MaxValue);

        SetTypeMap(DbType.StringFixedLength, "CHAR(255)");
        SetTypeMap(DbType.StringFixedLength, "CHAR($size)", CharLimit);
        SetTypeMap(DbType.StringFixedLength, "TEXT", TextLimit);
        SetTypeMap(DbType.StringFixedLength, "MEDIUMTEXT", MediumTextLimit);
        SetTypeMap(DbType.StringFixedLength, "LONGTEXT", int.MaxValue);
    }
}
