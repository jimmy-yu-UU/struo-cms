using FluentMigrator;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Migrations.Core;

/// <summary>Creates the framework tables: languages, files, media folders, identity, revisions, settings and sessions.</summary>
[Migration(Version, "CreateCoreSchema")]
public sealed class CreateCoreSchema : StruoMigration
{
    public const long Version = 202610080000;

    private const string Files = "files";
    private const string FileTranslations = "file_translations";
    private const string MediaFolders = "media_folders";
    private const string Users = "users";
    private const string Roles = "roles";
    private const string Permissions = "permissions";
    private const string UserRoles = "user_roles";
    private const string Revisions = "revisions";
    private const string UserSessions = "user_sessions";

    private const string CreatedAt = "createdat";
    private const string CreatedBy = "createdby";
    private const string UpdatedAt = "updatedat";
    private const string UpdatedBy = "updatedby";
    private const string RowVersion = "version";
    private const string RoleId = "roleid";
    private const string UserId = "userid";

    public override void Up()
    {
        Create.Table(FrameworkTable("languages"))
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("code").AsString(255).NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("isdefault").AsBoolean().NotNullable()
            .WithColumn("enabled").AsBoolean().NotNullable()
            .WithColumn("sort").AsInt32().NotNullable()
            .WithColumn(CreatedAt).AsDateTime().NotNullable()
            .WithColumn(CreatedBy).AsGuid().Nullable()
            .WithColumn(UpdatedAt).AsDateTime().NotNullable()
            .WithColumn(UpdatedBy).AsGuid().Nullable();

        Create.Table(FrameworkTable(Files))
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
            .WithColumn(CreatedAt).AsDateTime().NotNullable()
            .WithColumn(CreatedBy).AsGuid().Nullable()
            .WithColumn(UpdatedAt).AsDateTime().NotNullable()
            .WithColumn(UpdatedBy).AsGuid().Nullable()
            .WithColumn(RowVersion).AsInt64().NotNullable();

        Create.Index($"ix_{FrameworkTable(Files)}_folderid").OnTable(FrameworkTable(Files))
            .OnColumn("folderid").Ascending();

        Create.Table(FrameworkTable(FileTranslations))
            .WithColumn("id").AsInt64().PrimaryKey().Identity()
            .WithColumn("fileid").AsGuid().NotNullable()
            .WithColumn("locale").AsString(255).NotNullable()
            .WithColumn("title").AsString(255).Nullable()
            .WithColumn("alt").AsString(255).Nullable();

        CreateTranslationUniqueIndex(FrameworkTable(FileTranslations), "fileid", "locale");

        Create.Table(FrameworkTable(MediaFolders))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn(CreatedAt).AsShape(ColumnShape.TimestampWithTimeZone, Db).NotNullable()
            .WithColumn(UpdatedAt).AsShape(ColumnShape.TimestampWithTimeZone, Db).NotNullable()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("parentid").AsGuid().Nullable()
            .WithColumn(CreatedBy).AsGuid().Nullable()
            .WithColumn(UpdatedBy).AsGuid().Nullable()
            .WithColumn(RowVersion).AsInt64().NotNullable();

        Create.Index($"ix_{FrameworkTable(MediaFolders)}_parentid").OnTable(FrameworkTable(MediaFolders))
            .OnColumn("parentid").Ascending();

        Create.Table(FrameworkTable(Users))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("email").AsString(255).NotNullable()
            .WithColumn("password").AsString(255).NotNullable()
            .WithColumn("name").AsString(255).Nullable()
            .WithColumn("isactive").AsBoolean().NotNullable()
            .WithColumn("accesstoken").AsString(255).Nullable()
            .WithColumn("accesstokencreatedat").AsDateTime().Nullable()
            .WithColumn("accesstokenlastusedat").AsDateTime().Nullable()
            .WithColumn(CreatedAt).AsDateTime().NotNullable()
            .WithColumn(CreatedBy).AsGuid().Nullable()
            .WithColumn(UpdatedAt).AsDateTime().NotNullable()
            .WithColumn(UpdatedBy).AsGuid().Nullable()
            .WithColumn(RowVersion).AsInt64().NotNullable();

        Create.Index($"ux_{FrameworkTable(Users)}_email").OnTable(FrameworkTable(Users))
            .OnColumn("email").Ascending()
            .WithOptions().Unique();

        Create.Index($"ux_{FrameworkTable(Users)}_accesstoken").OnTable(FrameworkTable(Users))
            .OnColumn("accesstoken").Ascending()
            .WithOptions().Unique();

        Create.Table(FrameworkTable(Roles))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("name").AsString(255).NotNullable()
            .WithColumn("issuperadmin").AsBoolean().NotNullable()
            .WithColumn("description").AsString(255).Nullable()
            .WithColumn(CreatedAt).AsDateTime().NotNullable()
            .WithColumn(CreatedBy).AsGuid().Nullable()
            .WithColumn(UpdatedAt).AsDateTime().NotNullable()
            .WithColumn(UpdatedBy).AsGuid().Nullable()
            .WithColumn(RowVersion).AsInt64().NotNullable();

        Create.Index($"ux_{FrameworkTable(Roles)}_name").OnTable(FrameworkTable(Roles))
            .OnColumn("name").Ascending()
            .WithOptions().Unique();

        Create.Table(FrameworkTable(Permissions))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn(RoleId).AsGuid().NotNullable()
            .WithColumn("collection").AsString(255).NotNullable()
            .WithColumn("canread").AsBoolean().NotNullable()
            .WithColumn("canwrite").AsBoolean().NotNullable()
            .WithColumn("candelete").AsBoolean().NotNullable()
            .WithColumn(CreatedAt).AsDateTime().NotNullable()
            .WithColumn(CreatedBy).AsGuid().Nullable()
            .WithColumn(UpdatedAt).AsDateTime().NotNullable()
            .WithColumn(UpdatedBy).AsGuid().Nullable()
            .WithColumn(RowVersion).AsInt64().NotNullable();

        Create.Index($"ix_{FrameworkTable(Permissions)}_roleid").OnTable(FrameworkTable(Permissions))
            .OnColumn(RoleId).Ascending();

        Create.Index($"ux_{FrameworkTable(Permissions)}_role_collection").OnTable(FrameworkTable(Permissions))
            .OnColumn(RoleId).Ascending()
            .OnColumn("collection").Ascending()
            .WithOptions().Unique();

        Create.Table(FrameworkTable(UserRoles))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn(UserId).AsGuid().NotNullable()
            .WithColumn(RoleId).AsGuid().NotNullable()
            .WithColumn(CreatedAt).AsDateTime().NotNullable()
            .WithColumn(CreatedBy).AsGuid().Nullable()
            .WithColumn(UpdatedAt).AsDateTime().NotNullable()
            .WithColumn(UpdatedBy).AsGuid().Nullable()
            .WithColumn(RowVersion).AsInt64().NotNullable();

        Create.Index($"ix_{FrameworkTable(UserRoles)}_userid").OnTable(FrameworkTable(UserRoles))
            .OnColumn(UserId).Ascending();

        Create.Index($"ix_{FrameworkTable(UserRoles)}_roleid").OnTable(FrameworkTable(UserRoles))
            .OnColumn(RoleId).Ascending();

        Create.Index($"ux_{FrameworkTable(UserRoles)}_user_role").OnTable(FrameworkTable(UserRoles))
            .OnColumn(UserId).Ascending()
            .OnColumn(RoleId).Ascending()
            .WithOptions().Unique();

        Create.Table(FrameworkTable(Revisions))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("collectionname").AsString(255).NotNullable()
            .WithColumn("itemid").AsString(255).NotNullable()
            .WithColumn("revisionnumber").AsInt64().NotNullable()
            .WithColumn("operation").AsString(255).NotNullable()
            .WithColumn("sourcerevisionnumber").AsInt64().Nullable()
            .WithColumn("snapshot").AsLongText(Db).NotNullable()
            .WithColumn(CreatedAt).AsDateTime().NotNullable()
            .WithColumn(CreatedBy).AsGuid().Nullable();

        Create.Index($"ux_{FrameworkTable(Revisions)}_item_no").OnTable(FrameworkTable(Revisions))
            .OnColumn("collectionname").Ascending()
            .OnColumn("itemid").Ascending()
            .OnColumn("revisionnumber").Ascending()
            .WithOptions().Unique();

        Create.Table(FrameworkTable("site_settings"))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("brandname").AsLongText(Db).NotNullable()
            .WithColumn("logofileid").AsGuid().Nullable()
            .WithColumn(UpdatedAt).AsShape(ColumnShape.TimestampWithTimeZone, Db).NotNullable()
            .WithColumn(UpdatedBy).AsGuid().Nullable();

        Create.Table(FrameworkTable(UserSessions))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn(UserId).AsGuid().NotNullable()
            .WithColumn("ticketkey").AsString(255).NotNullable()
            .WithColumn(CreatedAt).AsShape(ColumnShape.TimestampWithTimeZone, Db).NotNullable()
            .WithColumn("expiresat").AsShape(ColumnShape.TimestampWithTimeZone, Db).NotNullable();

        Create.Index($"ix_{FrameworkTable(UserSessions)}_userid").OnTable(FrameworkTable(UserSessions))
            .OnColumn(UserId).Ascending();

        Create.Index($"ux_{FrameworkTable(UserSessions)}_ticketkey").OnTable(FrameworkTable(UserSessions))
            .OnColumn("ticketkey").Ascending()
            .WithOptions().Unique();
    }

    public override void Down()
    {
        Delete.Table(FrameworkTable(UserSessions));
        Delete.Table(FrameworkTable("site_settings"));
        Delete.Table(FrameworkTable(Revisions));
        Delete.Table(FrameworkTable(UserRoles));
        Delete.Table(FrameworkTable(Permissions));
        Delete.Table(FrameworkTable(Roles));
        Delete.Table(FrameworkTable(Users));
        Delete.Table(FrameworkTable(MediaFolders));
        Delete.Table(FrameworkTable(FileTranslations));
        Delete.Table(FrameworkTable(Files));
        Delete.Table(FrameworkTable("languages"));
    }
}
