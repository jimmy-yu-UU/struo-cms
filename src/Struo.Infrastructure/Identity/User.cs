using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Infrastructure.Identity;

/// <summary>
/// Framework-owned identity. Collection route is <c>user</c>. Credentials never leave the
/// server: <see cref="Password"/> stores the Argon2id PHC hash and <see cref="AccessToken"/>
/// stores the SHA-256 of the issued bearer token — both Hidden+ReadOnly so the generic CRUD
/// path can neither project nor accept them. PK is a <see cref="Guid"/> (UUIDv7), assigned by
/// the create flow.
/// </summary>
[SugarTable("users")]
[CmsCollection("User", Group = "System", DefaultDisplayField = nameof(Email), AdminOnly = true)]
public sealed class User : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [SugarColumn(UniqueGroupNameList = ["uq_users_email"])]
    [CmsField(Label = "Email", Interface = FieldInterface.Email, Required = true, Searchable = true, Sort = 1)]
    public string Email { get; set; } = "";

    [CmsField(Label = "Password", Interface = FieldInterface.Password, Hidden = true, ReadOnly = true, Sort = 2)]
    public string Password { get; set; } = "";

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Name", Interface = FieldInterface.Text, Sort = 3)]
    public string? Name { get; set; }

    [CmsField(Label = "Active", Interface = FieldInterface.Boolean, Sort = 4)]
    public bool IsActive { get; set; } = true;

    [SugarColumn(IsNullable = true, UniqueGroupNameList = ["uq_users_accesstoken"])]
    [CmsField(Label = "Access Token", Interface = FieldInterface.Text, Hidden = true, ReadOnly = true, Sort = 5)]
    public string? AccessToken { get; set; }

    // Roles are edited on the User form as a TagSelect (readable names), not by
    // hand-crafting userRole junction rows. Same M2M pattern as the sample's Article.Tags.
    [Navigate(typeof(UserRole), nameof(UserRole.UserId), nameof(UserRole.RoleId))]
    [CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Name}")]
    [SugarColumn(IsIgnore = true)]
    public List<Role> Roles { get; set; } = [];

    // Token lifecycle metadata. Internal columns — no [CmsField], so they never enter the generic
    // CRUD/read surface. The token stays permanent/non-expiring; these just record issuance and last
    // use so a leaked/stale token can be spotted and rotated.
    [SugarColumn(IsNullable = true)] public DateTime? AccessTokenCreatedAt { get; set; }
    [SugarColumn(IsNullable = true)] public DateTime? AccessTokenLastUsedAt { get; set; }
}
