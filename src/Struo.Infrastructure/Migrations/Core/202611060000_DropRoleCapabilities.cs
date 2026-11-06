using FluentMigrator;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Migrations.Core;

/// <summary>Drops the role capabilities table; its unique index goes with it. <see cref="Down"/> recreates both exactly as
/// <see cref="CreateRoleCapabilities"/> builds them.</summary>
[Migration(Version, "DropRoleCapabilities")]
public sealed class DropRoleCapabilities : StruoMigration
{
    public const long Version = 202611060000;

    private const string RoleCapabilities = "role_capabilities";

    private string IndexName => $"ux_{FrameworkTable(RoleCapabilities)}_role_capability";

    public override void Up() =>
        Delete.Table(FrameworkTable(RoleCapabilities));

    public override void Down()
    {
        Create.Table(FrameworkTable(RoleCapabilities))
            .WithColumn("id").AsGuid().PrimaryKey()
            .WithColumn("roleid").AsGuid().NotNullable()
            .WithColumn("capability").AsString(255).NotNullable()
            .WithColumn("createdat").AsTimestamp(Db).NotNullable()
            .WithColumn("createdby").AsGuid().Nullable()
            .WithColumn("updatedat").AsTimestamp(Db).NotNullable()
            .WithColumn("updatedby").AsGuid().Nullable()
            .WithColumn("version").AsInt64().NotNullable();

        Create.Index(IndexName).OnTable(FrameworkTable(RoleCapabilities))
            .OnColumn("roleid").Ascending()
            .OnColumn("capability").Ascending()
            .WithOptions().Unique();
    }
}
