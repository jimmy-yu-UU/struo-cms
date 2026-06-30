using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Identity;

/// <summary>Junction modelling the user↔role many-to-many. Collection route is <c>userRole</c>.
/// Unique on (<see cref="UserId"/>, <see cref="RoleId"/>).</summary>
[SugarTable("user_roles")]
[CmsCollection("UserRole", Group = "System", DefaultDisplayField = nameof(UserId))]
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
