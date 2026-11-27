using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Tests.Fixtures;

[SugarTable("fx_vault_translations")]
public sealed class FxVaultTranslation
{
    [SugarColumn(IsPrimaryKey = true, IsIdentity = true)] public long Id { get; set; }
    public Guid FxVaultId { get; set; }
    public string Locale { get; set; } = "";

    [CmsField(Label = "Title", Interface = FieldInterface.Text, Sort = 1)]
    public string Title { get; set; } = "";

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Secret cover", Interface = FieldInterface.Image, Hidden = true, Sort = 2)]
    public Guid? SecretCoverId { get; set; }
}
