using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Identity;

/// <summary>Framework-owned RBAC role. Collection route is <c>role</c>.
/// <see cref="IsSuperAdmin"/> short-circuits all permission checks (allow-all).</summary>
[SugarTable("roles")]
[SugarIndex("ux_{table}_name", nameof(Name), OrderByType.Asc, true)]
[CmsCollection("Role", Group = "System", DefaultDisplayField = nameof(Name), AdminOnly = true)]
public sealed class Role : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Searchable = true, Sort = 1)]
    public string Name { get; set; } = "";

    [CmsField(Label = "Super Admin", Interface = FieldInterface.Boolean, Sort = 2)]
    public bool IsSuperAdmin { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Description", Interface = FieldInterface.Text, Sort = 3)]
    public string? Description { get; set; }
}
