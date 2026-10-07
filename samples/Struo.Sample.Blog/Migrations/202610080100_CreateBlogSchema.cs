using FluentMigrator;
using Struo.Infrastructure.Migrations;

namespace Struo.Sample.Blog.Migrations;

/// <summary>Creates the five tables of the blog sample: articles, their translations, categories, tags and the article-tag junction.</summary>
[Migration(202610080100, "CreateBlogSchema")]
public sealed class CreateBlogSchema : StruoMigration
{
    public override void Up()
    {
        Create.Table("articles")
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
            .WithColumn("createdat").AsDateTime().NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsDateTime().NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();

        Create.Index("ix_articles_categoryid").OnTable("articles")
            .OnColumn("categoryid").Ascending();


        Create.Table("article_translations")
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("articleid").AsGuid().NotNullable()
            .WithColumn("locale").AsString(255).NotNullable()
            .WithColumn("title").AsString(255).NotNullable()
            .WithColumn("body").AsLongText(Db).Nullable()
            .WithColumn("internalslug").AsString(255).Nullable()
            .WithColumn("seotitle").AsString(255).Nullable()
            .WithColumn("seometadescription").AsLongText(Db).Nullable()
            .WithColumn("seoogimageid").AsGuid().Nullable();

        CreateTranslationUniqueIndex("article_translations", "articleid", "locale");


        Create.Table("categories")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("deletedat").AsDateTime().Nullable()
            .WithColumn("deletedby").AsGuid().Nullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("parentid").AsGuid().Nullable()
            .WithColumn("createdat").AsDateTime().NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsDateTime().NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();

        Create.Index("ix_categories_parentid").OnTable("categories")
            .OnColumn("parentid").Ascending();


        Create.Table("tags")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("createdat").AsDateTime().NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsDateTime().NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();


        Create.Table("article_tags")
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("articleid").AsGuid().NotNullable()
            .WithColumn("tagid").AsGuid().NotNullable()
            .WithColumn("note").AsString(255).Nullable()
            .WithColumn("sort").AsInt32().NotNullable();

        Create.Index("ix_article_tags_articleid").OnTable("article_tags")
            .OnColumn("articleid").Ascending();

        Create.Index("ix_article_tags_tagid").OnTable("article_tags")
            .OnColumn("tagid").Ascending();
    }

    public override void Down()
    {
        Delete.Table("article_tags");
        Delete.Table("tags");
        Delete.Table("categories");
        Delete.Table("article_translations");
        Delete.Table("articles");
    }
}
