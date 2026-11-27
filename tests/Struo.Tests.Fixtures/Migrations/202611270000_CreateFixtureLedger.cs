using FluentMigrator;
using Struo.Infrastructure.Migrations;

namespace Struo.Tests.Fixtures.Migrations;

/// <summary>Creates the ledger table, whose link keys are partly ReadOnly or audit fields.</summary>
[Migration(202611270000, "CreateFixtureLedger")]
public sealed class CreateFixtureLedger : StruoMigration
{
    private const string Ledgers = "fx_ledgers";

    public override void Up()
    {
        Create.Table(Ledgers)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("ownerid").AsGuid().Nullable()
            .WithColumn("partnerid").AsGuid().Nullable()
            .WithColumn("stampid").AsGuid().Nullable()
            .WithColumn("createdat").AsTimestamp(Db).NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsTimestamp(Db).NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();
    }

    public override void Down() => Delete.Table(Ledgers);
}
