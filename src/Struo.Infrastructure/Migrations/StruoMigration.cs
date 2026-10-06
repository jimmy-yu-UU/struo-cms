using FluentMigrator;
using FluentMigrator.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Configuration;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Migrations;

/// <summary>Base class for every StruoCMS migration. Exposes the active backend and the framework table prefix.</summary>
public abstract class StruoMigration : Migration
{
    private StruoMigrationContext? _struo;

    protected StruoDbType Db => Struo.DbType;

    /// <summary>The prefixed name of a framework table.</summary>
    protected string FrameworkTable(string name) => TableNaming.Apply(Struo.TablePrefix, name);

    private StruoMigrationContext Struo => _struo ?? throw new InvalidOperationException(
        $"{GetType().Name}: StruoMigration members are available only inside Up() and Down().");

    public override void GetUpExpressions(IMigrationContext context)
    {
        _struo = context.ServiceProvider.GetRequiredService<StruoMigrationContext>();
        base.GetUpExpressions(context);
    }

    public override void GetDownExpressions(IMigrationContext context)
    {
        _struo = context.ServiceProvider.GetRequiredService<StruoMigrationContext>();
        base.GetDownExpressions(context);
    }
}
