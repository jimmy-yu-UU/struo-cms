using FluentMigrator;
using Struo.Infrastructure.Migrations;

namespace Struo.Tests.Fixtures.Migrations;

/// <summary>
/// Creates the lexicon table, which points at the glossary collection through a many-to-one
/// foreign key and a many-to-many junction.
/// </summary>
[Migration(202611200000, "CreateFixtureLexicon")]
public sealed class CreateFixtureLexicon : StruoMigration
{
    private const string Lexicons = "fx_lexicons";
    private const string LexiconGlossaries = "fx_lexicon_glossaries";

    public override void Up()
    {
        Create.Table(Lexicons)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("glossaryid").AsGuid().Nullable()
            .WithColumn("createdat").AsTimestamp(Db).NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsTimestamp(Db).NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();

        Create.Index("ix_fx_lexicons_glossaryid").OnTable(Lexicons).OnColumn("glossaryid").Ascending();

        Create.Table(LexiconGlossaries)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("lexiconid").AsGuid().NotNullable()
            .WithColumn("glossaryid").AsGuid().NotNullable();

        Create.Index("ix_fx_lexicon_glossaries_lexiconid").OnTable(LexiconGlossaries).OnColumn("lexiconid").Ascending();
        Create.Index("ix_fx_lexicon_glossaries_glossaryid").OnTable(LexiconGlossaries).OnColumn("glossaryid").Ascending();
    }

    public override void Down()
    {
        Delete.Table(LexiconGlossaries);
        Delete.Table(Lexicons);
    }
}
