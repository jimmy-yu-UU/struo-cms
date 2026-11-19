using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Tests.Fixtures;

// The lexicon<->glossary many-to-many junction.
[SugarTable("fx_lexicon_glossaries")]
[SugarIndex("ix_fx_lexicon_glossaries_lexiconid", nameof(LexiconId), OrderByType.Asc)]
[SugarIndex("ix_fx_lexicon_glossaries_glossaryid", nameof(GlossaryId), OrderByType.Asc)]
[CmsCollection("Lexicon glossary", Icon = "book", Group = "Content", Hidden = true)]
public sealed class FxLexiconGlossary
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    [CmsField(Label = "Lexicon", Interface = FieldInterface.Uuid, Required = true, Sort = 1)]
    public Guid LexiconId { get; set; }

    [CmsField(Label = "Glossary", Interface = FieldInterface.Uuid, Required = true, Sort = 2)]
    public Guid GlossaryId { get; set; }
}
