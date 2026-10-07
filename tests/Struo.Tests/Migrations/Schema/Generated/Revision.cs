using FluentMigrator;
using Struo.Infrastructure.Migrations;

namespace Struo.Tests.Migrations.Schema.Generated.Revision;

[Migration(202610071002, "CreateRevisions")]
public sealed class CreateRevisions : StruoMigration
{
    public override void Up()
    {
        Create.Table(FrameworkTable("revisions"))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("collectionname").AsString(255).NotNullable()
            .WithColumn("itemid").AsString(255).NotNullable()
            .WithColumn("revisionnumber").AsInt64().NotNullable()
            .WithColumn("operation").AsString(255).NotNullable()
            .WithColumn("sourcerevisionnumber").AsInt64().Nullable()
            .WithColumn("snapshot").AsLongText(Db).NotNullable()
            .WithColumn("createdat").AsDateTime().NotNullable()
            .WithColumn("createdby").AsGuid().Nullable();

        Create.Index($"ux_{FrameworkTable("revisions")}_item_no").OnTable(FrameworkTable("revisions"))
            .OnColumn("collectionname").Ascending()
            .OnColumn("itemid").Ascending()
            .OnColumn("revisionnumber").Ascending()
            .WithOptions().Unique();
    }

    public override void Down()
    {
        Delete.Table(FrameworkTable("revisions"));
    }
}
