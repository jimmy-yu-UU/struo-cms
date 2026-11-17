using System.Data;
using System.Reflection;
using AwesomeAssertions;
using FluentMigrator.Runner.Generators.MySql;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations;
using Xunit;

namespace Struo.Tests.Migrations.Utf8;

public sealed class Utf8Mb4TypeMapTests
{
    private static string Mysql(DbType type, int? size) => Utf8Mb4TypeRendering.ColumnSql(StruoDbType.MySql, type, size);

    [Theory]
    [InlineData(null, "VARCHAR(255)")]
    [InlineData(1, "VARCHAR(1)")]
    [InlineData(255, "VARCHAR(255)")]
    [InlineData(8192, "VARCHAR(8192)")]
    [InlineData(8193, "TEXT")]
    [InlineData(65535, "TEXT")]
    [InlineData(65536, "MEDIUMTEXT")]
    [InlineData(16777215, "MEDIUMTEXT")]
    [InlineData(16777216, "LONGTEXT")]
    [InlineData(int.MaxValue, "LONGTEXT")]
    public void MySql_AsString_renders_a_type_without_a_character_set(int? size, string type) =>
        Mysql(DbType.String, size).Should().Be($"ALTER TABLE `t` ADD COLUMN `c` {type};");

    [Theory]
    [InlineData(null, "CHAR(255)")]
    [InlineData(36, "CHAR(36)")]
    [InlineData(255, "CHAR(255)")]
    [InlineData(256, "TEXT")]
    [InlineData(65535, "TEXT")]
    [InlineData(65536, "MEDIUMTEXT")]
    [InlineData(16777215, "MEDIUMTEXT")]
    [InlineData(16777216, "LONGTEXT")]
    [InlineData(int.MaxValue, "LONGTEXT")]
    public void MySql_AsFixedLengthString_renders_a_type_without_a_character_set(int? size, string type) =>
        Mysql(DbType.StringFixedLength, size).Should().Be($"ALTER TABLE `t` ADD COLUMN `c` {type};");

    [Fact]
    public void Every_template_key_of_the_framework_map_is_restated_without_a_national_type()
    {
        var baseMap = Templates(new MySql8TypeMap());
        var ours = Templates(new Utf8Mb4MySqlTypeMap());

        foreach (var type in new[] { DbType.String, DbType.StringFixedLength })
        {
            ours[type].Keys.Should().Equal(baseMap[type].Keys, $"{type} must restate every key");
            ours[type].Values.Should().OnlyContain(t => !t.Contains("CHARACTER SET") && !t.StartsWith('N'));
        }
    }

    [Fact]
    public void Types_other_than_the_two_string_types_keep_the_framework_templates()
    {
        var baseMap = Templates(new MySql8TypeMap());
        var ours = Templates(new Utf8Mb4MySqlTypeMap());

        foreach (var type in baseMap.Keys.Except([DbType.String, DbType.StringFixedLength]))
            ours[type].Should().Equal(baseMap[type], $"{type} is not a text type");
    }

    [Theory]
    [InlineData(StruoDbType.Sqlite, DbType.String, 255, "ALTER TABLE \"t\" ADD COLUMN \"c\" TEXT;")]
    [InlineData(StruoDbType.Sqlite, DbType.String, int.MaxValue, "ALTER TABLE \"t\" ADD COLUMN \"c\" TEXT;")]
    [InlineData(StruoDbType.PostgreSQL, DbType.String, 255, "ALTER TABLE \"public\".\"t\" ADD \"c\" varchar(255);")]
    [InlineData(StruoDbType.PostgreSQL, DbType.String, null, "ALTER TABLE \"public\".\"t\" ADD \"c\" text;")]
    [InlineData(StruoDbType.PostgreSQL, DbType.String, int.MaxValue, "ALTER TABLE \"public\".\"t\" ADD \"c\" text;")]
    [InlineData(StruoDbType.PostgreSQL, DbType.StringFixedLength, 36, "ALTER TABLE \"public\".\"t\" ADD \"c\" char(36);")]
    [InlineData(StruoDbType.SqlServer, DbType.String, 255, "ALTER TABLE [dbo].[t] ADD [c] NVARCHAR(255);")]
    [InlineData(StruoDbType.SqlServer, DbType.String, 8193, "ALTER TABLE [dbo].[t] ADD [c] NVARCHAR(MAX);")]
    [InlineData(StruoDbType.SqlServer, DbType.String, int.MaxValue, "ALTER TABLE [dbo].[t] ADD [c] NVARCHAR(MAX);")]
    [InlineData(StruoDbType.SqlServer, DbType.StringFixedLength, 36, "ALTER TABLE [dbo].[t] ADD [c] NCHAR(36);")]
    public void The_other_backends_render_what_the_framework_renders(
        StruoDbType db, DbType type, int? size, string expected) =>
        Utf8Mb4TypeRendering.ColumnSql(db, type, size).Should().Be(expected);

    private static Dictionary<DbType, SortedList<int, string>> Templates(object map)
    {
        var field = typeof(MySql8TypeMap).BaseType!.BaseType!.BaseType!
            .GetField("_templates", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var dictionary = (System.Collections.IDictionary)field.GetValue(map)!;
        var result = new Dictionary<DbType, SortedList<int, string>>();
        foreach (System.Collections.DictionaryEntry entry in dictionary)
            result[(DbType)entry.Key] = (SortedList<int, string>)entry.Value!;
        return result;
    }
}
