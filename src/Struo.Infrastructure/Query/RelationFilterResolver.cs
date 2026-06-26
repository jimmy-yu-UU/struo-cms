// src/Struo.Infrastructure/Query/RelationFilterResolver.cs
using System.Reflection;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;
using Struo.Infrastructure.Metadata;

namespace Struo.Infrastructure.Query;

public sealed class RelationFilterResolver(
    IItemRepository repository,
    RelationshipGraph graph,
    IMetadataProvider metadata,
    IEntityRegistry registry,
    StruoQueryOptions options) : IRelationFilterResolver
{
    public async Task<FilterNode?> RewriteAsync(
        string rootCollection,
        FilterNode? filter,
        string? queryLocale = null,
        CancellationToken ct = default)
    {
        switch (filter)
        {
            case null:
                return null;
            case ComparisonFilter c when RelationPath.IsRelationPath(c.FieldPath):
                var ids = await ResolveRootIdsAsync(rootCollection, c, ct);
                return ids.Count == 0
                    ? new ComparisonFilter("id", QueryOperator.Null, null)        // always-false PK leaf
                    : new ComparisonFilter("id", QueryOperator.In, ids);
            case ComparisonFilter cf when queryLocale is not null
                                         && IsTranslatableField(rootCollection, cf.FieldPath):
                // Translatable own-collection leaf: resolve to parent ids via the translation sidecar
                // at the query locale.
                var tIds = await ResolveTranslatableIdsAsync(rootCollection, cf, queryLocale, ct);
                return tIds.Count == 0
                    ? new ComparisonFilter("id", QueryOperator.Null, null)
                    : new ComparisonFilter("id", QueryOperator.In, tIds);
            case ComparisonFilter:
                return filter;                                                     // own-collection leaf
            case LogicalFilter l:
                var children = new List<FilterNode>(l.Children.Count);
                foreach (var ch in l.Children)
                    children.Add((await RewriteAsync(rootCollection, ch, queryLocale, ct))!);
                return new LogicalFilter(l.Op, children);
            default:
                return filter;
        }
    }

    /// <summary>
    /// Returns true when <paramref name="fieldName"/> is a translatable field on the given collection.
    /// </summary>
    private bool IsTranslatableField(string collection, string fieldName)
    {
        var meta = metadata.GetCollection(collection);
        if (meta?.Translation is null) return false;
        return meta.Translation.Fields.Any(
            f => string.Equals(f, fieldName, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resolves a translatable own-collection <see cref="ComparisonFilter"/> to a set of parent ids
    /// by querying the translation sidecar where Locale == queryLocale AND fieldCol op value.
    /// </summary>
    private async Task<IReadOnlyList<object>> ResolveTranslatableIdsAsync(
        string collection, ComparisonFilter c, string queryLocale, CancellationToken ct)
    {
        var meta = metadata.GetCollection(collection)!;
        var tm = meta.Translation!;

        // The field condition uses the camelCase field name (e.g. "title").
        // QueryTranslationParentIdsAsync will map it to the CLR property name on the translation entity.
        var fieldCondition = new ComparisonFilter(c.FieldPath, c.Op, c.Value);

        return await repository.QueryTranslationParentIdsAsync(
            tm.TranslationEntityType,
            tm.ForeignKeyProperty,
            tm.LocaleProperty,
            queryLocale,
            fieldCondition,
            ct);
    }

    private async Task<IReadOnlyList<object>> ResolveRootIdsAsync(
        string rootCollection, ComparisonFilter c, CancellationToken ct)
    {
        var path = RelationPath.Parse(rootCollection, c.FieldPath, graph, metadata, options.MaxRelationDepth);

        // Leaf: ids in the terminal collection matching "leaf <op> value".
        var leafCondition = new ComparisonFilter(path.LeafField, c.Op, c.Value);
        IReadOnlyList<object> set = await repository.QueryIdsAsync(path.TerminalCollection, leafCondition, ct);

        // Walk back leaf -> root, one hop per segment.
        for (var i = path.Segments.Count - 1; i >= 0; i--)
        {
            if (set.Count == 0) return set;
            set = await HopAsync(path.Segments[i], set, ct);
        }
        return set;
    }

    private async Task<IReadOnlyList<object>> HopAsync(
        RelationSegment seg, IReadOnlyList<object> targetIds, CancellationToken ct)
    {
        var desc = graph.Descriptors(seg.DeclaringCollection)
            .First(d => string.Equals(d.Meta.Name, seg.RelationName, StringComparison.OrdinalIgnoreCase));

        switch (seg.Relation.Kind)
        {
            case RelationKind.ManyToOne:
            {
                // declaring rows where FK IN targetIds -> their ids
                var declaringType = registry.Get(seg.DeclaringCollection)!.EntityType;
                var fkClr = registry.Get(seg.DeclaringCollection)!.FieldToProperty
                    .TryGetValue(seg.Relation.ForeignKey!, out var p) ? p
                    : throw new QueryException($"Foreign key '{seg.Relation.ForeignKey}' is not a known property on '{seg.DeclaringCollection}'.");
                var parents = await repository.QueryEntityWhereInAsync(declaringType, fkClr, targetIds, ct);
                return ReadIds(parents, "id");
            }
            case RelationKind.OneToMany:
            {
                // target(child) rows whose id IN targetIds -> read the reverse FK -> declaring (parent) ids
                var children = await repository.QueryWhereInAsync(seg.Relation.TargetCollection, "id", targetIds, ct);
                return ReadIds(children, desc.ReverseForeignKeyProperty!);
            }
            case RelationKind.ManyToMany:
            {
                // junction rows whose targetFk IN targetIds -> read parentFk -> declaring ids
                var junctions = await repository.QueryEntityWhereInAsync(desc.JunctionType!, desc.JunctionTargetFk!, targetIds, ct);
                return ReadIds(junctions, desc.JunctionParentFk!);
            }
            default:
                throw new QueryException($"Unsupported relation kind '{seg.Relation.Kind}'.");
        }
    }

    private static IReadOnlyList<object> ReadIds(IReadOnlyList<object> rows, string property) =>
        rows.Select(r => ReadProp(r, property)).Where(v => v is not null).Distinct().ToList()!;

    private static object? ReadProp(object entity, string property) =>
        entity.GetType().GetProperty(property,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)?.GetValue(entity);

}
