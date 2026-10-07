using SqlSugar;
using Struo.Domain.Metadata.Attributes;
using Struo.Domain.Metadata.Enums;

namespace Struo.Sample.Blog;

// The article<->tag M2M junction, promoted to a [CmsCollection] so the shipped sample demonstrates
// the junction-payload feature end to end: Note is extra data carried BY the link itself (not by
// either endpoint), and Sort lets an admin order an article's tags (wired via
// Article.Tags' [CmsRelation(SortField = nameof(Sort))]). Hidden = true keeps it out of the admin
// sidebar — it is still reachable through /api/schema, the RBAC matrix and GraphQL, same as any
// other collection; only the sidebar treats it specially. Both foreign keys must stay declared as
// writable [CmsField]s (Interface = FieldInterface.Uuid): MetadataScanner.ValidateJunctionCollections
// fails fast at startup on any junction collection whose FKs aren't writable, because the generic
// CRUD API would otherwise create rows with empty keys. A fork that deletes this sample (per
// AGENTS.md's core/sample boundary) loses nothing else — no core code references this type. The
// sample migration Migrations/202610080100_CreateBlogSchema.cs creates this table.
[SugarTable("article_tags")]
// Secondary indexes on both M2M junction FKs, both directions (RelationExpander expansion +
// sync-on-write); the sample migration creates them.
[SugarIndex("ix_article_tags_articleid", nameof(ArticleId), OrderByType.Asc)]
[SugarIndex("ix_article_tags_tagid", nameof(TagId), OrderByType.Asc)]
[CmsCollection("Article tag", Icon = "tag", Group = "Content", Hidden = true)]
public sealed class ArticleTag
{
    [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; }

    [CmsField(Label = "Article", Interface = FieldInterface.Uuid, Required = true, Sort = 1)]
    public Guid ArticleId { get; set; }

    [CmsField(Label = "Tag", Interface = FieldInterface.Uuid, Required = true, Sort = 2)]
    public Guid TagId { get; set; }

    [SugarColumn(IsNullable = true)]
    [CmsField(Label = "Note", Interface = FieldInterface.Text, MaxLength = 200, Sort = 3)]
    public string? Note { get; set; }

    [CmsField(Label = "Sort", Interface = FieldInterface.Number, Sort = 4)]
    public int Sort { get; set; }
}
