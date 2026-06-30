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
[CmsCollection("User", Group = "System", DefaultDisplayField = nameof(Email))]
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
}
