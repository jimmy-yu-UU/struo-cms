using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Tests.Fixtures;

// A collection whose file fields are Hidden (root and translated), and whose Files field is nullable.
[SugarTable("fx_vaults")]
[CmsCollection("Vault", Icon = "lock", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class FxVault : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [CmsTranslations(typeof(FxVaultTranslation))]
    [SugarColumn(IsIgnore = true)]
    public List<FxVaultTranslation> Translations { get; set; } = [];

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Sort = 1)]
    public string Name { get; set; } = "";

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Secret image", Interface = FieldInterface.Image, Hidden = true, Sort = 2)]
    public Guid? SecretImageId { get; set; }

    [CmsField(Label = "Secret gallery", Interface = FieldInterface.Files, Hidden = true, Sort = 3)]
    public List<Guid> SecretGallery { get; set; } = [];

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Shelf", Interface = FieldInterface.Files, Sort = 4)]
    public List<Guid>? Shelf { get; set; }
}
