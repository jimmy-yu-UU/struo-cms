using FluentMigrator;
using Struo.Infrastructure.Migrations;

namespace Struo.Tests.Fixtures.Migrations;

/// <summary>
/// Creates the stash table, whose Hidden fields hold structured values, and the glossary tables,
/// whose translation sidecar has a translatable Hidden field.
/// </summary>
[Migration(202611160000, "CreateFixtureStructuredFields")]
public sealed class CreateFixtureStructuredFields : StruoMigration
{
    private const string Stashes = "fx_stashes";
    private const string Glossaries = "fx_glossaries";
    private const string GlossaryTranslations = "fx_glossary_translations";
    private const string TranslationGlossaryId = "fxglossaryid";

    public override void Up()
    {
        Create.Table(Stashes)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("labels").AsJson(Db).NotNullable()
            .WithColumn("numbers").AsJson(Db).NotNullable()
            .WithColumn("scores").AsJson(Db).NotNullable()
            .WithColumn("note").AsJson(Db).NotNullable()
            .WithColumn("createdat").AsTimestamp(Db).NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsTimestamp(Db).NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();

        Create.Table(Glossaries)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("createdat").AsTimestamp(Db).NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsTimestamp(Db).NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();

        Create.Table(GlossaryTranslations)
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn(TranslationGlossaryId).AsGuid().NotNullable()
            .WithColumn("locale").AsString(255).NotNullable()
            .WithColumn("title").AsString(255).NotNullable()
            .WithColumn("aliases").AsJson(Db).NotNullable();

        CreateTranslationUniqueIndex(GlossaryTranslations, TranslationGlossaryId, "locale");
    }

    public override void Down()
    {
        Delete.Table(GlossaryTranslations);
        Delete.Table(Glossaries);
        Delete.Table(Stashes);
    }
}
