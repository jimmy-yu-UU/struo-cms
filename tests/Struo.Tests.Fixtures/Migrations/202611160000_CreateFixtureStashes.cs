using FluentMigrator;
using Struo.Infrastructure.Migrations;

namespace Struo.Tests.Fixtures.Migrations;

/// <summary>Creates the stash table, whose Hidden field holds a list.</summary>
[Migration(202611160000, "CreateFixtureStashes")]
public sealed class CreateFixtureStashes : StruoMigration
{
    private const string Stashes = "fx_stashes";

    public override void Up()
    {
        Create.Table(Stashes)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("labels").AsJson(Db).NotNullable()
            .WithColumn("createdat").AsTimestamp(Db).NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsTimestamp(Db).NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();
    }

    public override void Down() => Delete.Table(Stashes);
}
