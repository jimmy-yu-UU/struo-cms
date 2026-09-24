using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Identity;

/// <summary>Junction modelling the user↔role many-to-many. Collection route is <c>userRole</c>.
/// Unique on (<see cref="UserId"/>, <see cref="RoleId"/>).</summary>
[SugarTable("user_roles")]
// RBAC effective-permission resolution scans both FKs on every authenticated request; CodeFirst
// creates these indexes wherever this table does not already exist. Separate single-column btrees
// (distinct from the uq_user_roles_user_role composite unique below, whose leading column serves only
// userid lookups).
[SugarIndex("ix_{table}_userid", nameof(UserId), OrderByType.Asc)]
[SugarIndex("ix_{table}_roleid", nameof(RoleId), OrderByType.Asc)]
[SugarIndex("ux_{table}_user_role", nameof(UserId), OrderByType.Asc, nameof(RoleId), OrderByType.Asc, true)]
[CmsCollection("UserRole", Group = "System", DefaultDisplayField = nameof(UserId), AdminOnly = true, Hidden = true)]
public sealed class UserRole : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [CmsField(Label = "User", Interface = FieldInterface.Text, Required = true, Sort = 1)]
    public Guid UserId { get; set; }

    [CmsField(Label = "Role", Interface = FieldInterface.Text, Required = true, Sort = 2)]
    public Guid RoleId { get; set; }
}
