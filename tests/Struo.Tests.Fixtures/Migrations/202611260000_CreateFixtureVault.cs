using FluentMigrator;
using Struo.Infrastructure.Migrations;

namespace Struo.Tests.Fixtures.Migrations;

/// <summary>Creates the vault table, whose file fields are Hidden, and its translation sidecar.</summary>
[Migration(202611260000, "CreateFixtureVault")]
public sealed class CreateFixtureVault : StruoMigration
{
    private const string Vaults = "fx_vaults";
    private const string VaultTranslations = "fx_vault_translations";
    private const string TranslationVaultId = "fxvaultid";

    public override void Up()
    {
        Create.Table(Vaults)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("secretimageid").AsGuid().Nullable()
            .WithColumn("secretgallery").AsJson(Db).NotNullable()
            .WithColumn("shelf").AsJson(Db).Nullable()
            .WithColumn("createdat").AsTimestamp(Db).NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsTimestamp(Db).NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();

        Create.Table(VaultTranslations)
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn(TranslationVaultId).AsGuid().NotNullable()
            .WithColumn("locale").AsString(255).NotNullable()
            .WithColumn("title").AsString(255).NotNullable()
            .WithColumn("secretcoverid").AsGuid().Nullable();

        CreateTranslationUniqueIndex(VaultTranslations, TranslationVaultId, "locale");
    }

    public override void Down()
    {
        Delete.Table(VaultTranslations);
        Delete.Table(Vaults);
    }
}
