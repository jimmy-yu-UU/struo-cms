using System.Data;
using AwesomeAssertions;
using FluentMigrator.Expressions;
using FluentMigrator.Model;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations.Core;
using Xunit;

namespace Struo.Tests.Migrations.Utf8;

public sealed class ConvertStringColumnsToUtf8Mb4Tests
{
    [Theory]
    [InlineData(StruoDbType.Sqlite)]
    [InlineData(StruoDbType.PostgreSQL)]
    [InlineData(StruoDbType.SqlServer)]
    [InlineData(StruoDbType.Oracle)]
    public void The_migration_produces_no_expression_off_MySql(StruoDbType db) =>
        MigrationExpressions.Up(new ConvertStringColumnsToUtf8Mb4(), db).Should().BeEmpty();

    [Fact]
    public void On_MySql_the_migration_alters_22_string_columns_and_nothing_else()
    {
        var expressions = MigrationExpressions.Up(new ConvertStringColumnsToUtf8Mb4(), StruoDbType.MySql);

        expressions.Should().HaveCount(22).And.AllBeOfType<AlterColumnExpression>();
    }

    [Fact]
    public void Every_alteration_restates_a_string_column_the_creating_migration_declares()
    {
        var created = StringColumnsOf(new CreateCoreSchema());
        var altered = MigrationExpressions.Up(new ConvertStringColumnsToUtf8Mb4(), StruoDbType.MySql)
            .Cast<AlterColumnExpression>()
            .Where(e => !e.TableName.EndsWith("schema_versions", StringComparison.Ordinal))
            .Select(e => (e.TableName, e.Column.Name, e.Column.Size, e.Column.IsNullable, e.Column.Type))
            .ToList();

        altered.Should().BeEquivalentTo(created);
        altered.Should().OnlyContain(c => c.Type == DbType.String);
    }

    [Fact]
    public void The_version_table_description_is_restated_as_nullable_1024()
    {
        var version = MigrationExpressions.Up(new ConvertStringColumnsToUtf8Mb4(), StruoDbType.MySql)
            .Cast<AlterColumnExpression>().Single(e => e.TableName == "struo_schema_versions");

        (version.Column.Name, version.Column.Size, version.Column.IsNullable, version.Column.Type)
            .Should().Be(("description", 1024, true, DbType.String));
    }

    [Fact]
    public void No_alteration_restates_a_default_because_no_creating_declaration_has_one()
    {
        MigrationExpressions.Up(new CreateCoreSchema(), StruoDbType.MySql)
            .OfType<CreateTableExpression>().SelectMany(t => t.Columns)
            .Where(c => c.Type == DbType.String)
            .Should().OnlyContain(c => c.DefaultValue is ColumnDefinition.UndefinedDefaultValue);

        MigrationExpressions.Up(new ConvertStringColumnsToUtf8Mb4(), StruoDbType.MySql)
            .Cast<AlterColumnExpression>().Should().OnlyContain(e => e.Column.DefaultValue is ColumnDefinition.UndefinedDefaultValue);
    }

    [Fact]
    public void The_tables_with_colliding_unique_values_come_first()
    {
        var tables = MigrationExpressions.Up(new ConvertStringColumnsToUtf8Mb4(), StruoDbType.MySql)
            .Cast<AlterColumnExpression>().Select(e => e.TableName).Distinct().ToList();

        tables.Take(2).Should().Equal("struo_users", "struo_roles");
    }

    private static List<(string, string, int?, bool?, DbType?)> StringColumnsOf(CreateCoreSchema migration) =>
        MigrationExpressions.Up(migration, StruoDbType.MySql)
            .OfType<CreateTableExpression>()
            .SelectMany(t => t.Columns.Where(c => c.Type == DbType.String)
                .Select(c => (t.TableName, c.Name, c.Size, c.IsNullable, c.Type)))
            .ToList();
}
