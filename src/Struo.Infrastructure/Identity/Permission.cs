using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Identity;

/// <summary>A role's read/write/delete grant for one collection. Collection route is
/// <c>permission</c>. Unique on (<see cref="RoleId"/>, <see cref="Collection"/>).</summary>
[SugarTable("permissions")]
// RBAC effective-permission resolution scans permissions by roleid on every authenticated request;
// CodeFirst creates this index wherever this table does not already exist.
[SugarIndex("ix_{table}_roleid", nameof(RoleId), OrderByType.Asc)]
[SugarIndex("ux_{table}_role_collection", nameof(RoleId), OrderByType.Asc, nameof(Collection), OrderByType.Asc, true)]
[CmsCollection("Permission", Group = "System", DefaultDisplayField = nameof(Collection), AdminOnly = true, Hidden = true)]
public sealed class Permission : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [CmsField(Label = "Role", Interface = FieldInterface.Text, Required = true, Sort = 1)]
    public Guid RoleId { get; set; }

    [CmsField(Label = "Collection", Interface = FieldInterface.Text, Required = true, Sort = 2)]
    public string Collection { get; set; } = "";

    [CmsField(Label = "Can Read", Interface = FieldInterface.Boolean, Sort = 3)]
    public bool CanRead { get; set; }

    [CmsField(Label = "Can Write", Interface = FieldInterface.Boolean, Sort = 4)]
    public bool CanWrite { get; set; }

    [CmsField(Label = "Can Delete", Interface = FieldInterface.Boolean, Sort = 5)]
    public bool CanDelete { get; set; }
}
