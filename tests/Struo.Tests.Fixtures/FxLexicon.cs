using SqlSugar;
using Struo.Domain.Auditing;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Tests.Fixtures;

// A collection without a translation sidecar that points at a collection with one, through a
// many-to-one and a many-to-many relation.
[SugarTable("fx_lexicons")]
[SugarIndex("ix_fx_lexicons_glossaryid", nameof(GlossaryId), OrderByType.Asc)]
[CmsCollection("Lexicon", Icon = "book", Group = "Content", DefaultDisplayField = nameof(Name))]
public sealed class FxLexicon : AuditableEntity
{
    [SugarColumn(IsPrimaryKey = true)] public override Guid Id { get; set; }

    [CmsField(Label = "Name", Interface = FieldInterface.Text, Required = true, Sort = 1)]
    public string Name { get; set; } = "";

    [SugarColumn(IsNullable = true)]
    public Guid? GlossaryId { get; set; }

    [Navigate(NavigateType.OneToOne, nameof(GlossaryId))]
    [CmsRelation(Interface = RelationInterface.Dropdown, DisplayTemplate = "{Id}", OnDelete = OnDelete.SetNull)]
    [SugarColumn(IsIgnore = true)]
    public FxGlossary? Glossary { get; set; }

    [Navigate(typeof(FxLexiconGlossary), nameof(FxLexiconGlossary.LexiconId), nameof(FxLexiconGlossary.GlossaryId))]
    [CmsRelation(Interface = RelationInterface.TagSelect, DisplayTemplate = "{Id}")]
    [SugarColumn(IsIgnore = true)]
    public List<FxGlossary> Glossaries { get; set; } = [];
}
