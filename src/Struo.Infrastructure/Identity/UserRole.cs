using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Identity;

/// <summary>Junction modelling the user↔role many-to-many. Collection route is <c>userRole</c>.
/// Unique on (<see cref="UserId"/>, <see cref="RoleId"/>).</summary>
[SugarTable("user_roles")]
// DB-5: CodeFirst parity with db/migrations/009-hot-path-indexes.sql — RBAC effective-permission
// resolution scans both FKs per authenticated request. Separate single-column btrees (distinct from
// the uq_user_roles_user_role composite unique below, whose leading column serves only userid lookups).
[SugarIndex("ix_user_roles_userid", nameof(UserId), OrderByType.Asc)]
[SugarIndex("ix_user_roles_roleid", nameof(RoleId), OrderByType.Asc)]
[CmsCollection("UserRole", Group = "System", DefaultDisplayField = nameof(UserId), AdminOnly = true)]
public sealed class UserRole : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [SugarColumn(UniqueGroupNameList = ["uq_user_roles_user_role"])]
    [CmsField(Label = "User", Interface = FieldInterface.Text, Required = true, Sort = 1)]
    public Guid UserId { get; set; }

    [SugarColumn(UniqueGroupNameList = ["uq_user_roles_user_role"])]
    [CmsField(Label = "Role", Interface = FieldInterface.Text, Required = true, Sort = 2)]
    public Guid RoleId { get; set; }
}
