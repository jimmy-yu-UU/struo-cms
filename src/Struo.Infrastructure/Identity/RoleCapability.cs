using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Identity;

/// <summary>One system capability a role holds (see <c>SystemCapabilities</c>). Collection route is
/// <c>roleCapability</c>. Unique on (<see cref="RoleId"/>, <see cref="Capability"/>); that index also
/// serves the lookup by role.</summary>
[SugarTable("role_capabilities")]
[SugarIndex("ux_{table}_role_capability", nameof(RoleId), OrderByType.Asc, nameof(Capability), OrderByType.Asc, true)]
[CmsCollection("RoleCapability", Group = "System", DefaultDisplayField = nameof(Capability), AdminOnly = true, Hidden = true)]
public sealed class RoleCapability : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [CmsField(Label = "Role", Interface = FieldInterface.Text, Required = true, Sort = 1)]
    public Guid RoleId { get; set; }

    [CmsField(Label = "Capability", Interface = FieldInterface.Text, Required = true, Sort = 2)]
    public string Capability { get; set; } = "";
}
