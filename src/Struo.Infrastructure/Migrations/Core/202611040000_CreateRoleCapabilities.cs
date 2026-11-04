using FluentMigrator;
using Struo.Infrastructure.Persistence;

namespace Struo.Infrastructure.Migrations.Core;

/// <summary>Creates the role capabilities table: the system capabilities each role holds.</summary>
[Migration(Version, "CreateRoleCapabilities")]
public sealed class CreateRoleCapabilities : StruoMigration
{
    public const long Version = 202611040000;

    private const string RoleCapabilities = "role_capabilities";

    private string IndexName => $"ux_{FrameworkTable(RoleCapabilities)}_role_capability";

    public override void Up()
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

    public override void Down() =>
        Delete.Table(FrameworkTable(RoleCapabilities));
}
