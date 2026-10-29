using FluentMigrator;
using Struo.Infrastructure.Migrations;

namespace Struo.Tests.Fixtures.Migrations;

/// <summary>Creates the five fixture tables: articles, their translations, categories, tags and the article-tag junction.</summary>
[Migration(202610080200, "CreateFixtureSchema")]
public sealed class CreateFixtureSchema : StruoMigration
{
    private const string Categories = "fx_categories";
    private const string Articles = "fx_articles";
    private const string ArticleTranslations = "fx_article_translations";
    private const string ArticleTags = "fx_article_tags";
    private const string Tags = "fx_tags";

    private const string CreatedAt = "createdat";
    private const string CreatedBy = "createdby";
    private const string UpdatedAt = "updatedat";
    private const string UpdatedBy = "updatedby";
    private const string RowVersion = "version";
    private const string ArticleId = "articleid";
    private const string TranslationArticleId = "fxarticleid";

    public override void Up()
    {
        Create.Table(Articles)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("deletedat").AsTimestamp(Db).Nullable()
            .WithColumn("deletedby").AsGuid().Nullable()
            .WithColumn("status").AsString(255).NotNullable()
            .WithColumn("publishedat").AsTimestamp(Db).Nullable()
            .WithColumn("heroimageid").AsGuid().Nullable()
            .WithColumn("regions").AsJson(Db).NotNullable()
            .WithColumn("audiences").AsJson(Db).NotNullable()
            .WithColumn("keywords").AsJson(Db).NotNullable()
            .WithColumn("attributes").AsLongText(Db).Nullable()
            .WithColumn("meta").AsJson(Db).NotNullable()
            .WithColumn("gallery").AsJson(Db).NotNullable()
            .WithColumn("faqs").AsJson(Db).NotNullable()
            .WithColumn("internalnote").AsString(255).Nullable()
            .WithColumn("categoryid").AsGuid().Nullable()
            .WithColumn(CreatedAt).AsTimestamp(Db).NotNullable()
            .WithColumn(CreatedBy).AsGuid().Nullable()
            .WithColumn(UpdatedAt).AsTimestamp(Db).NotNullable()
            .WithColumn(UpdatedBy).AsGuid().Nullable()
            .WithColumn(RowVersion).AsInt64().NotNullable();

        Create.Index("ix_fx_articles_categoryid").OnTable(Articles)
            .OnColumn("categoryid").Ascending();


        Create.Table(ArticleTranslations)
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn(TranslationArticleId).AsGuid().NotNullable()
            .WithColumn("locale").AsString(255).NotNullable()
            .WithColumn("title").AsString(255).NotNullable()
            .WithColumn("body").AsLongText(Db).Nullable()
            .WithColumn("internalslug").AsString(255).Nullable()
            .WithColumn("seotitle").AsString(255).Nullable()
            .WithColumn("seometadescription").AsLongText(Db).Nullable()
            .WithColumn("seoogimageid").AsGuid().Nullable();

        CreateTranslationUniqueIndex(ArticleTranslations, TranslationArticleId, "locale");


        Create.Table(Categories)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("deletedat").AsTimestamp(Db).Nullable()
            .WithColumn("deletedby").AsGuid().Nullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("parentid").AsGuid().Nullable()
            .WithColumn(CreatedAt).AsTimestamp(Db).NotNullable()
            .WithColumn(CreatedBy).AsGuid().Nullable()
            .WithColumn(UpdatedAt).AsTimestamp(Db).NotNullable()
            .WithColumn(UpdatedBy).AsGuid().Nullable()
            .WithColumn(RowVersion).AsInt64().NotNullable();

        Create.Index("ix_fx_categories_parentid").OnTable(Categories)
            .OnColumn("parentid").Ascending();


        Create.Table(Tags)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn(CreatedAt).AsTimestamp(Db).NotNullable()
            .WithColumn(CreatedBy).AsGuid().Nullable()
            .WithColumn(UpdatedAt).AsTimestamp(Db).NotNullable()
            .WithColumn(UpdatedBy).AsGuid().Nullable()
            .WithColumn(RowVersion).AsInt64().NotNullable();


        Create.Table(ArticleTags)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn(ArticleId).AsGuid().NotNullable()
            .WithColumn("tagid").AsGuid().NotNullable()
            .WithColumn("note").AsString(255).Nullable()
            .WithColumn("sort").AsInt32().NotNullable();

        Create.Index("ix_fx_article_tags_articleid").OnTable(ArticleTags)
            .OnColumn(ArticleId).Ascending();

        Create.Index("ix_fx_article_tags_tagid").OnTable(ArticleTags)
            .OnColumn("tagid").Ascending();
    }

    public override void Down()
    {
        Delete.Table(ArticleTags);
        Delete.Table(Tags);
        Delete.Table(Categories);
        Delete.Table(ArticleTranslations);
        Delete.Table(Articles);
    }
}
