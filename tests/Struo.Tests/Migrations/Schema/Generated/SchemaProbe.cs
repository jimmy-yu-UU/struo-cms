using FluentMigrator;
using Struo.Infrastructure.Migrations;
using Struo.Infrastructure.Persistence;

namespace Struo.Tests.Migrations.Schema.Generated.SchemaProbe;

[Migration(202610071001, "CreateSchemaProbe")]
public sealed class CreateSchemaProbe : StruoMigration
{
    public override void Up()
    {
        Create.Table("schema_probe")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("code").AsString(255).NotNullable()
            .WithColumn("displayname").AsString(80).NotNullable()
            .WithColumn("note").AsString(255).Nullable()
            .WithColumn("body").AsLongText(Db).NotNullable()
            .WithColumn("tags").AsJson(Db).NotNullable()
            .WithColumn("createdat").AsShape(ColumnShape.TimestampWithTimeZone, Db).NotNullable()
            .WithColumn("stamp").AsShape(ColumnShape.TimestampWithTimeZone, Db).NotNullable()
            .WithColumn("kind").AsInt32().NotNullable()
            .WithColumn("price").AsDecimal().NotNullable()
            .WithColumn("amount").AsDecimal(10, 2).NotNullable()
            .WithColumn("ratio").AsDouble().NotNullable()
            .WithColumn("flag").AsBoolean().NotNullable()
            .WithColumn("ref").AsGuid().NotNullable()
            .WithColumn("blob").AsBinary(int.MaxValue).NotNullable();

        Create.Index("ux_schema_probe_code").OnTable("schema_probe")
            .OnColumn("code").Ascending()
            .WithOptions().Unique();

        Create.Index("ix_schema_probe_name_kind").OnTable("schema_probe")
            .OnColumn("displayname").Ascending()
            .OnColumn("kind").Ascending();
    }

    public override void Down()
    {
        Delete.Table("schema_probe");
    }
}
