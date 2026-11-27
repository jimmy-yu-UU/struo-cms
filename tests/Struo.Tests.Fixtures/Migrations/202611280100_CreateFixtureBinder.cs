using FluentMigrator;
using Struo.Infrastructure.Migrations;

namespace Struo.Tests.Fixtures.Migrations;

/// <summary>Creates the binder table and its junction table, whose parent key is ReadOnly.</summary>
[Migration(202611280100, "CreateFixtureBinder")]
public sealed class CreateFixtureBinder : StruoMigration
{
    private const string Binders = "fx_binders";
    private const string BinderLinks = "fx_binder_links";

    public override void Up()
    {
        Create.Table(Binders)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("createdat").AsTimestamp(Db).NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsTimestamp(Db).NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();

        Create.Table(BinderLinks)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("binderid").AsGuid().NotNullable()
            .WithColumn("tagid").AsGuid().NotNullable();
    }

    public override void Down()
    {
        Delete.Table(BinderLinks);
        Delete.Table(Binders);
    }
}
