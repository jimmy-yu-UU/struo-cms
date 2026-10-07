using FluentMigrator;
using Struo.Infrastructure.Migrations;

namespace Struo.Sample.Blog.Migrations;

/// <summary>Creates the five tables of the blog sample: articles, their translations, categories, tags and the article-tag junction.</summary>
[Migration(202610080100, "CreateBlogSchema")]
public sealed class CreateBlogSchema : StruoMigration
{
    private const string Categories = "categories";
    private const string Articles = "articles";
    private const string ArticleTranslations = "article_translations";
    private const string ArticleTags = "article_tags";

    private const string CreatedAt = "createdat";
    private const string CreatedBy = "createdby";
    private const string UpdatedAt = "updatedat";
    private const string UpdatedBy = "updatedby";
    private const string RowVersion = "version";
    private const string ArticleId = "articleid";

    public override void Up()
    {
        Create.Table(Articles)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("deletedat").AsDateTime().Nullable()
            .WithColumn("deletedby").AsGuid().Nullable()
            .WithColumn("status").AsString(255).NotNullable()
            .WithColumn("publishedat").AsDateTime().Nullable()
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
            .WithColumn(CreatedAt).AsDateTime().NotNullable()
            .WithColumn(CreatedBy).AsGuid().Nullable()
            .WithColumn(UpdatedAt).AsDateTime().NotNullable()
            .WithColumn(UpdatedBy).AsGuid().Nullable()
            .WithColumn(RowVersion).AsInt64().NotNullable();

        Create.Index("ix_articles_categoryid").OnTable(Articles)
            .OnColumn("categoryid").Ascending();


        Create.Table(ArticleTranslations)
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn(ArticleId).AsGuid().NotNullable()
            .WithColumn("locale").AsString(255).NotNullable()
            .WithColumn("title").AsString(255).NotNullable()
            .WithColumn("body").AsLongText(Db).Nullable()
            .WithColumn("internalslug").AsString(255).Nullable()
            .WithColumn("seotitle").AsString(255).Nullable()
            .WithColumn("seometadescription").AsLongText(Db).Nullable()
            .WithColumn("seoogimageid").AsGuid().Nullable();

        CreateTranslationUniqueIndex(ArticleTranslations, ArticleId, "locale");


        Create.Table(Categories)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("deletedat").AsDateTime().Nullable()
            .WithColumn("deletedby").AsGuid().Nullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("parentid").AsGuid().Nullable()
            .WithColumn(CreatedAt).AsDateTime().NotNullable()
            .WithColumn(CreatedBy).AsGuid().Nullable()
            .WithColumn(UpdatedAt).AsDateTime().NotNullable()
            .WithColumn(UpdatedBy).AsGuid().Nullable()
            .WithColumn(RowVersion).AsInt64().NotNullable();

        Create.Index("ix_categories_parentid").OnTable(Categories)
            .OnColumn("parentid").Ascending();


        Create.Table("tags")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn(CreatedAt).AsDateTime().NotNullable()
            .WithColumn(CreatedBy).AsGuid().Nullable()
            .WithColumn(UpdatedAt).AsDateTime().NotNullable()
            .WithColumn(UpdatedBy).AsGuid().Nullable()
            .WithColumn(RowVersion).AsInt64().NotNullable();


        Create.Table(ArticleTags)
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn(ArticleId).AsGuid().NotNullable()
            .WithColumn("tagid").AsGuid().NotNullable()
            .WithColumn("note").AsString(255).Nullable()
            .WithColumn("sort").AsInt32().NotNullable();

        Create.Index("ix_article_tags_articleid").OnTable(ArticleTags)
            .OnColumn(ArticleId).Ascending();

        Create.Index("ix_article_tags_tagid").OnTable(ArticleTags)
            .OnColumn("tagid").Ascending();
    }

    public override void Down()
    {
        Delete.Table(ArticleTags);
        Delete.Table("tags");
        Delete.Table(Categories);
        Delete.Table(ArticleTranslations);
        Delete.Table(Articles);
    }
}
