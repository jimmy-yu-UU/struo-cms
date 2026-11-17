using System.Data;
using FluentMigrator.Expressions;
using FluentMigrator.Model;
using FluentMigrator.Runner;
using FluentMigrator.Runner.Generators;
using FluentMigrator;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations;

namespace Struo.Tests.Migrations.Utf8;

/// <summary>Renders one column through the generator the migration host registers for a backend.</summary>
internal static class Utf8Mb4TypeRendering
{
    public static string ColumnSql(StruoDbType db, DbType type, int? size)
    {
        var services = new ServiceCollection()
            .AddFluentMigratorCore()
            .ConfigureRunner(rb => MigrationDialect.Register(rb, db));
        using var provider = services.BuildServiceProvider(validateScopes: false);
        using var scope = provider.CreateScope();
        var generator = scope.ServiceProvider.GetRequiredService<IMigrationGenerator>();
        var expression = new CreateColumnExpression
        {
            TableName = "t",
            Column = new ColumnDefinition { Name = "c", Type = type, Size = size, IsNullable = true },
        };
        return generator.Generate(expression);
    }
}
