using FluentMigrator;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Migrations.Core;

/// <summary>Creates the framework tables: languages, files, media folders, identity, revisions, settings and sessions.</summary>
[Migration(Version, "CreateCoreSchema")]
public sealed class CreateCoreSchema : StruoMigration
{
    public const long Version = 202610080000;

    public override void Up()
    {
        Create.Table(FrameworkTable("languages"))
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("code").AsString(255).NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("isdefault").AsBoolean().NotNullable()
            .WithColumn("enabled").AsBoolean().NotNullable()
            .WithColumn("sort").AsInt32().NotNullable()
            .WithColumn("createdat").AsDateTime().NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsDateTime().NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable();

        Create.Table(FrameworkTable("files"))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("storagekey").AsString(255).NotNullable()
            .WithColumn("filename").AsString(255).NotNullable()
            .WithColumn("contenttype").AsString(255).NotNullable()
            .WithColumn("size").AsInt64().NotNullable()
            .WithColumn("width").AsInt32().Nullable()
            .WithColumn("height").AsInt32().Nullable()
            .WithColumn("status").AsString(255).NotNullable()
            .WithColumn("folderid").AsGuid().Nullable()
            .WithColumn("deletedat").AsDateTime().Nullable()
            .WithColumn("deletedby").AsGuid().Nullable()
            .WithColumn("createdat").AsDateTime().NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsDateTime().NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();

        Create.Index($"ix_{FrameworkTable("files")}_folderid").OnTable(FrameworkTable("files"))
            .OnColumn("folderid").Ascending();

        Create.Table(FrameworkTable("file_translations"))
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("fileid").AsGuid().NotNullable()
            .WithColumn("locale").AsString(255).NotNullable()
            .WithColumn("title").AsString(255).Nullable()
            .WithColumn("alt").AsString(255).Nullable();

        CreateTranslationUniqueIndex("file_translations", FrameworkTable("file_translations"), "fileid", "locale");

        Create.Table(FrameworkTable("media_folders"))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("createdat").AsShape(ColumnShape.TimestampWithTimeZone, Db).NotNullable()
            .WithColumn("updatedat").AsShape(ColumnShape.TimestampWithTimeZone, Db).NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("parentid").AsGuid().Nullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();

        Create.Index($"ix_{FrameworkTable("media_folders")}_parentid").OnTable(FrameworkTable("media_folders"))
            .OnColumn("parentid").Ascending();

        Create.Table(FrameworkTable("users"))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("email").AsString(255).NotNullable()
            .WithColumn("password").AsString(255).NotNullable()
            .WithColumn("name").AsString(255).Nullable()
            .WithColumn("isactive").AsBoolean().NotNullable()
            .WithColumn("accesstoken").AsString(255).Nullable()
            .WithColumn("accesstokencreatedat").AsDateTime().Nullable()
            .WithColumn("accesstokenlastusedat").AsDateTime().Nullable()
            .WithColumn("createdat").AsDateTime().NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsDateTime().NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();

        Create.Index($"ux_{FrameworkTable("users")}_email").OnTable(FrameworkTable("users"))
            .OnColumn("email").Ascending()
            .WithOptions().Unique();

        Create.Index($"ux_{FrameworkTable("users")}_accesstoken").OnTable(FrameworkTable("users"))
            .OnColumn("accesstoken").Ascending()
            .WithOptions().Unique();

        Create.Table(FrameworkTable("roles"))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("issuperadmin").AsBoolean().NotNullable()
            .WithColumn("description").AsString(255).Nullable()
            .WithColumn("createdat").AsDateTime().NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsDateTime().NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();

        Create.Index($"ux_{FrameworkTable("roles")}_name").OnTable(FrameworkTable("roles"))
            .OnColumn("name").Ascending()
            .WithOptions().Unique();

        Create.Table(FrameworkTable("permissions"))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("roleid").AsGuid().NotNullable()
            .WithColumn("collection").AsString(255).NotNullable()
            .WithColumn("canread").AsBoolean().NotNullable()
            .WithColumn("canwrite").AsBoolean().NotNullable()
            .WithColumn("candelete").AsBoolean().NotNullable()
            .WithColumn("createdat").AsDateTime().NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsDateTime().NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();

        Create.Index($"ix_{FrameworkTable("permissions")}_roleid").OnTable(FrameworkTable("permissions"))
            .OnColumn("roleid").Ascending();

        Create.Index($"ux_{FrameworkTable("permissions")}_role_collection").OnTable(FrameworkTable("permissions"))
            .OnColumn("roleid").Ascending()
            .OnColumn("collection").Ascending()
            .WithOptions().Unique();

        Create.Table(FrameworkTable("user_roles"))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("userid").AsGuid().NotNullable()
            .WithColumn("roleid").AsGuid().NotNullable()
            .WithColumn("createdat").AsDateTime().NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsDateTime().NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();

        Create.Index($"ix_{FrameworkTable("user_roles")}_userid").OnTable(FrameworkTable("user_roles"))
            .OnColumn("userid").Ascending();

        Create.Index($"ix_{FrameworkTable("user_roles")}_roleid").OnTable(FrameworkTable("user_roles"))
            .OnColumn("roleid").Ascending();

        Create.Index($"ux_{FrameworkTable("user_roles")}_user_role").OnTable(FrameworkTable("user_roles"))
            .OnColumn("userid").Ascending()
            .OnColumn("roleid").Ascending()
            .WithOptions().Unique();

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

        Create.Table(FrameworkTable("site_settings"))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("brandname").AsLongText(Db).NotNullable()
            .WithColumn("logofileid").AsGuid().Nullable()
            .WithColumn("updatedat").AsShape(ColumnShape.TimestampWithTimeZone, Db).NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable();

        Create.Table(FrameworkTable("user_sessions"))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("userid").AsGuid().NotNullable()
            .WithColumn("ticketkey").AsString(255).NotNullable()
            .WithColumn("createdat").AsShape(ColumnShape.TimestampWithTimeZone, Db).NotNullable()
            .WithColumn("expiresat").AsShape(ColumnShape.TimestampWithTimeZone, Db).NotNullable();

        Create.Index($"ix_{FrameworkTable("user_sessions")}_userid").OnTable(FrameworkTable("user_sessions"))
            .OnColumn("userid").Ascending();

        Create.Index($"ux_{FrameworkTable("user_sessions")}_ticketkey").OnTable(FrameworkTable("user_sessions"))
            .OnColumn("ticketkey").Ascending()
            .WithOptions().Unique();
    }

    public override void Down()
    {
        Delete.Table(FrameworkTable("user_sessions"));
        Delete.Table(FrameworkTable("site_settings"));
        Delete.Table(FrameworkTable("revisions"));
        Delete.Table(FrameworkTable("user_roles"));
        Delete.Table(FrameworkTable("permissions"));
        Delete.Table(FrameworkTable("roles"));
        Delete.Table(FrameworkTable("users"));
        Delete.Table(FrameworkTable("media_folders"));
        Delete.Table(FrameworkTable("file_translations"));
        Delete.Table(FrameworkTable("files"));
        Delete.Table(FrameworkTable("languages"));
    }
}
