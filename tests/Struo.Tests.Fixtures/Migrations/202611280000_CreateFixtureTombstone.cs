using FluentMigrator;
using Struo.Infrastructure.Migrations;

namespace Struo.Tests.Fixtures.Migrations;

/// <summary>Creates the tombstone table, a soft-deletable collection with a relation on its DeletedBy column.</summary>
[Migration(202611280000, "CreateFixtureTombstone")]
public sealed class CreateFixtureTombstone : StruoMigration
{
    private const string Tombstones = "fx_tombstones";

    public override void Up()
    {
        Create.Table(Tombstones)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("deletedat").AsTimestamp(Db).Nullable()
            .WithColumn("deletedby").AsGuid().Nullable()
            .WithColumn("createdat").AsTimestamp(Db).NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsTimestamp(Db).NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();
    }

    public override void Down() => Delete.Table(Tombstones);
}
