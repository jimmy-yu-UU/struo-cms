// src/Struo.Infrastructure/Query/OrderByExpressionBuilder.cs
using System.Text;
using SqlSugar;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

/// <summary>
/// Builds SQL ORDER BY expressions for item queries: plain columns, locale-scoped translatable
/// fields (correlated subquery), and to-one relation paths (correlated subquery with one JOIN per
/// hop). Extracted verbatim from <see cref="SqlSugarItemRepository"/> (ARC-4) — zero behavior
/// change; the SQL text is moved unchanged, including the locale-literal quote-doubling guard.
/// </summary>
public sealed class OrderByExpressionBuilder(
    ISqlSugarClient db,
    IEntityRegistry registry,
    IRelationshipGraph graph,
    IMetadataProvider metadata,
    StruoQueryOptions options)
{
    public string? BuildOrderBy(IReadOnlyList<SortField> sort, EntityDescriptor d, string collection, string? queryLocale = null)
    {
        if (sort.Count == 0) return null;
        var collMeta = metadata.GetCollection(collection);
        var translatableFields = collMeta?.Translation?.Fields ?? [];

        var parts = sort.Select(s =>
        {
            if (RelationPath.IsRelationPath(s.Field))
                return $"{RelationOrderExpr(collection, s.Field)} {(s.Descending ? "DESC" : "ASC")}";

            // Translatable sort field: emit a locale-scoped correlated subquery.
            if (queryLocale is not null
                && translatableFields.Any(f => string.Equals(f, s.Field, StringComparison.OrdinalIgnoreCase))
                && collMeta?.Translation is not null)
            {
                return $"{TranslatableOrderExpr(collection, s.Field, collMeta.Translation, queryLocale, d)} {(s.Descending ? "DESC" : "ASC")}";
            }

            var prop = d.FieldToProperty.TryGetValue(s.Field, out var p) ? p : s.Field;
            var col = db.EntityMaintenance.GetDbColumnName(prop, d.EntityType);
            return $"{col} {(s.Descending ? "DESC" : "ASC")}";
        });
        return string.Join(", ", parts);
    }

    /// <summary>
    /// Builds a locale-scoped correlated subquery ORDER BY expression for a translatable field:
    /// <c>(SELECT t.{fieldCol} FROM {translationTable} t WHERE t.{fk} = {parentTable}.{id} AND t.{localeCol} = '{locale}')</c>
    /// Mirrors the <see cref="RelationOrderExpr"/> form used for cross-relation sort.
    /// </summary>
    private string TranslatableOrderExpr(
        string collection, string fieldName,
        Domain.Metadata.Models.TranslationMetadata tm,
        string queryLocale,
        EntityDescriptor parentDesc)
    {
        var translationType = tm.TranslationEntityType;

        // Translation table name and column names via EntityMaintenance (no raw SQL names hardcoded).
        var translationTable = db.EntityMaintenance.GetTableName(translationType);
        var fkCol = db.EntityMaintenance.GetDbColumnName(tm.ForeignKeyProperty, translationType);
        var localeCol = db.EntityMaintenance.GetDbColumnName(tm.LocaleProperty, translationType);

        // Resolve camelCase field name -> CLR property -> DB column on the translation entity.
        var fieldClr = translationType
            .GetProperty(fieldName, System.Reflection.BindingFlags.Public |
                                    System.Reflection.BindingFlags.Instance |
                                    System.Reflection.BindingFlags.IgnoreCase)?.Name ?? fieldName;
        var fieldCol = db.EntityMaintenance.GetDbColumnName(fieldClr, translationType);

        // Parent table and its PK column.
        var parentTable = db.EntityMaintenance.GetTableName(parentDesc.EntityType);
        var parentIdCol = db.EntityMaintenance.GetDbColumnName(parentDesc.IdProperty, parentDesc.EntityType);

        // Defense-in-depth: escape single quotes in the locale literal (SQL standard doubling)
        // so that even a crafted locale code that slipped past the charset validator cannot break
        // out of the literal and inject SQL.  The charset validator in ItemService is the primary
        // guard; this is the secondary sink-level guard.
        var safeLocale = queryLocale.Replace("'", "''");
        return $"(SELECT t.{fieldCol} FROM {translationTable} t WHERE t.{fkCol} = {parentTable}.{parentIdCol} AND t.{localeCol} = '{safeLocale}')";
    }

    /// <summary>
    /// Builds a correlated-subquery ORDER BY expression for a to-one relation path (validated
    /// all-to-one by QueryValidator before reaching here). This is the spike-validated form:
    /// ONE subquery with ONE JOIN per extra hop. The root table is referenced by its bare
    /// table name (SqlSugar uses no alias for a single-table Queryable&lt;T&gt;).
    ///   category.name        -> (SELECT t1.name FROM categories t1 WHERE t1.id = articles.category_id)
    ///   category.parent.name -> (SELECT t2.name FROM categories t1
    ///                             JOIN categories t2 ON t2.id = t1.parent_id
    ///                            WHERE t1.id = articles.category_id)
    /// </summary>
    private string RelationOrderExpr(string rootCollection, string path)
    {
        var rp = RelationPath.Parse(rootCollection, path, graph, metadata, options.MaxRelationDepth);
        var segs = rp.Segments;

        string FkCol(EntityDescriptor d, string camelFk)
        {
            var clr = d.FieldToProperty.TryGetValue(camelFk, out var p) ? p
                : throw new QueryException($"Foreign key '{camelFk}' is not a known property on '{d.EntityType.Name}'.");
            return db.EntityMaintenance.GetDbColumnName(clr, d.EntityType);
        }

        var rootDesc = registry.Get(rootCollection)!;
        var rootTable = db.EntityMaintenance.GetTableName(rootDesc.EntityType);

        var t1Desc = registry.Get(segs[0].Relation.TargetCollection)!;
        var t1Table = db.EntityMaintenance.GetTableName(t1Desc.EntityType);
        var t1IdCol = db.EntityMaintenance.GetDbColumnName(t1Desc.IdProperty, t1Desc.EntityType);

        var lastDesc = registry.Get(segs[^1].Relation.TargetCollection)!;
        var leafClr = lastDesc.FieldToProperty.TryGetValue(rp.LeafField, out var lp) ? lp : rp.LeafField;
        var leafCol = db.EntityMaintenance.GetDbColumnName(leafClr, lastDesc.EntityType);

        var sb = new StringBuilder();
        sb.Append($"(SELECT t{segs.Count}.{leafCol} FROM {t1Table} t1");

        var prevDesc = t1Desc;  // FK linking t{k} to t{k+1} lives on the previous entity
        for (var k = 1; k < segs.Count; k++)
        {
            var segDesc = registry.Get(segs[k].Relation.TargetCollection)!;
            var segTable = db.EntityMaintenance.GetTableName(segDesc.EntityType);
            var segIdCol = db.EntityMaintenance.GetDbColumnName(segDesc.IdProperty, segDesc.EntityType);
            var linkFkCol = FkCol(prevDesc, segs[k].Relation.ForeignKey!);
            sb.Append($" JOIN {segTable} t{k + 1} ON t{k + 1}.{segIdCol} = t{k}.{linkFkCol}");
            prevDesc = segDesc;
        }

        sb.Append($" WHERE t1.{t1IdCol} = {rootTable}.{FkCol(rootDesc, segs[0].Relation.ForeignKey!)})");
        return sb.ToString();
    }
}
