using System.Reflection;
using FluentMigrator;
using FluentMigrator.Expressions;
using FluentMigrator.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Configuration;
using Struo.Infrastructure.Migrations;

namespace Struo.Tests.Migrations.Utf8;

/// <summary>The expressions a migration's Up() produces for a backend, without a database.</summary>
internal static class MigrationExpressions
{
    public static IReadOnlyList<IMigrationExpression> Up(IMigration migration, StruoDbType db)
    {
        var services = new ServiceCollection()
            .AddSingleton(new StruoMigrationContext(db, "struo_", null))
            .BuildServiceProvider();
        var context = new MigrationContext(DispatchProxy.Create<IQuerySchema, NoSchemaProxy>(), services, "");
        migration.GetUpExpressions(context);
        return context.Expressions.ToList();
    }

    public class NoSchemaProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            targetMethod!.ReturnType.IsValueType ? Activator.CreateInstance(targetMethod.ReturnType) : null;
    }
}
