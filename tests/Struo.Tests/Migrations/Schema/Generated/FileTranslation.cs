using FluentMigrator;
using Struo.Infrastructure.Migrations;

namespace Struo.Tests.Migrations.Schema.Generated.FileTranslation;

[Migration(202610071003, "CreateFileTranslations")]
public sealed class CreateFileTranslations : StruoMigration
{
    public override void Up()
    {
        Create.Table(FrameworkTable("file_translations"))
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("fileid").AsGuid().NotNullable()
            .WithColumn("locale").AsString(255).NotNullable()
            .WithColumn("title").AsString(255).Nullable()
            .WithColumn("alt").AsString(255).Nullable();

        CreateTranslationUniqueIndex(FrameworkTable("file_translations"), "fileid", "locale");
    }

    public override void Down()
    {
        Delete.Table(FrameworkTable("file_translations"));
    }
}
