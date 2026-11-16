using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;

namespace Struo.Tests.Fixtures;

[SugarTable("fx_glossaries")]
[CmsCollection("Glossary", Icon = "book", Group = "Content")]
public sealed class FxGlossary : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [CmsTranslations(typeof(FxGlossaryTranslation))]
    [SugarColumn(IsIgnore = true)]
    public List<FxGlossaryTranslation> Translations { get; set; } = [];
}
