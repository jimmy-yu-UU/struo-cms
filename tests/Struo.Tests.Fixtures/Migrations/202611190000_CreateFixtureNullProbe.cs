using FluentMigrator;
using Struo.Infrastructure.Migrations;

namespace Struo.Tests.Fixtures.Migrations;

/// <summary>
/// Creates the null-probe tables: a collection whose NOT NULL and nullable columns take null from a
/// write, its translation sidecar, a junction with a NOT NULL payload column and a collection with NOT NULL
/// structured columns on itself and on its sidecar.
/// </summary>
[Migration(202611190000, "CreateFixtureNullProbe")]
public sealed class CreateFixtureNullProbe : StruoMigration
{
    private const string Probes = "fx_null_probes";
    private const string ProbeTranslations = "fx_null_probe_translations";
    private const string ProbeTags = "fx_null_probe_tags";
    private const string Shelves = "fx_null_shelves";
    private const string ShelfTranslations = "fx_null_shelf_translations";
    private const string TranslationProbeId = "fxnullprobeid";
    private const string TranslationShelfId = "fxnullshelfid";

    public override void Up()
    {
        Create.Table(Probes)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("plain").AsString(255).NotNullable()
            .WithColumn("defaulted").AsString(255).NotNullable()
            .WithColumn("optional").AsString(255).Nullable()
            .WithColumn("declared").AsString(255).Nullable()
            .WithColumn("createdat").AsTimestamp(Db).NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsTimestamp(Db).NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();

        Create.Table(ProbeTranslations)
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn(TranslationProbeId).AsGuid().NotNullable()
            .WithColumn("locale").AsString(255).NotNullable()
            .WithColumn("heading").AsString(255).NotNullable();

        CreateTranslationUniqueIndex(ProbeTranslations, TranslationProbeId, "locale");

        Create.Table(ProbeTags)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("probeid").AsGuid().NotNullable()
            .WithColumn("tagid").AsGuid().NotNullable()
            .WithColumn("remark").AsString(255).NotNullable()
            .WithColumn("note").AsString(255).NotNullable();

        Create.Table(Shelves)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("labels").AsJson(Db).NotNullable()
            .WithColumn("createdat").AsTimestamp(Db).NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsTimestamp(Db).NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();

        Create.Table(ShelfTranslations)
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn(TranslationShelfId).AsGuid().NotNullable()
            .WithColumn("locale").AsString(255).NotNullable()
            .WithColumn("heading").AsString(255).NotNullable()
            .WithColumn("aliases").AsJson(Db).NotNullable();

        CreateTranslationUniqueIndex(ShelfTranslations, TranslationShelfId, "locale");
    }

    public override void Down()
    {
        Delete.Table(ShelfTranslations);
        Delete.Table(Shelves);
        Delete.Table(ProbeTags);
        Delete.Table(ProbeTranslations);
        Delete.Table(Probes);
    }
}
