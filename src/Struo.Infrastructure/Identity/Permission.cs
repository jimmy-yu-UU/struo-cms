using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Identity;

/// <summary>A role's read/write/delete grant for one collection. Collection route is
/// <c>permission</c>. Unique on (<see cref="RoleId"/>, <see cref="Collection"/>).</summary>
[SugarTable("permissions")]
// CodeFirst parity with db/migrations/001-core-baseline.sql — RBAC effective-permission
// resolution scans permissions by roleid per authenticated request.
[SugarIndex("ix_permissions_roleid", nameof(RoleId), OrderByType.Asc)]
[CmsCollection("Permission", Group = "System", DefaultDisplayField = nameof(Collection), AdminOnly = true, Hidden = true)]
public sealed class Permission : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [SugarColumn(UniqueGroupNameList = ["uq_permissions_role_collection"])]
    [CmsField(Label = "Role", Interface = FieldInterface.Text, Required = true, Sort = 1)]
    public Guid RoleId { get; set; }

    [SugarColumn(UniqueGroupNameList = ["uq_permissions_role_collection"])]
    [CmsField(Label = "Collection", Interface = FieldInterface.Text, Required = true, Sort = 2)]
    public string Collection { get; set; } = "";

    [CmsField(Label = "Can Read", Interface = FieldInterface.Boolean, Sort = 3)]
    public bool CanRead { get; set; }

    [CmsField(Label = "Can Write", Interface = FieldInterface.Boolean, Sort = 4)]
    public bool CanWrite { get; set; }

    [CmsField(Label = "Can Delete", Interface = FieldInterface.Boolean, Sort = 5)]
    public bool CanDelete { get; set; }
}
