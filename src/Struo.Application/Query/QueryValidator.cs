// src/Struo.Application/Query/QueryValidator.cs
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

public static class QueryValidator
{
    public static QueryModel Validate(
        QueryModel q, CollectionMetadata meta, StruoQueryOptions opts,
        IRelationshipGraph graph, IMetadataProvider metadata, IPermissionService permissions)
    {
        // Allowlist: a collection's own fields, plus its declared many-to-one relation
        // foreign keys (e.g. "categoryId" on article) so callers (incl. the frontend
        // RelatedList) can filter/sort by the FK column even though it carries no
        // [CmsField]. Never widened to arbitrary non-relation columns.
        //
        // Hidden fields are excluded on purpose: they hold credentials (User.Password /
        // User.AccessToken) that projection already refuses to serialize. If they stayed
        // filterable/sortable, meta.total would become a blind-extraction oracle
        // (?filter[password][_startsWith]=...) that leaks the value one character at a time.
        var known = meta.Fields.Where(f => !f.Hidden).Select(f => f.Name)
            .Concat(meta.Relations
                .Where(r => r.Kind == RelationKind.ManyToOne && r.ForeignKey is not null)
                .Select(r => r.ForeignKey!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        void CheckField(string path, bool forSort = false, bool allowRelation = true)
        {
            if (RelationPath.IsRelationPath(path))
            {
                if (!allowRelation)
                    throw new QueryException($"Relation paths are not supported in field selection: '{path}'.");
                // The caller's read grant on the root collection does not extend across a relation
                // hop: every collection the path traverses needs its own. Without this, a caller
                // granted only 'article' could read 'user' rows through article.author, and
                // meta.total on filter[author.email][_starts_with] would enumerate them one
                // character at a time.
                //
                // This runs BEFORE RelationPath.Parse, not after, and resolves hops itself to do
                // so. Parse validates the leaf against the terminal collection's metadata and names
                // the collection and field in its QueryException, which DomainErrorMap returns
                // verbatim; letting it run first would let an anonymous caller with one public-read
                // collection enumerate the field names of every collection reachable from it. An
                // unresolvable hop breaks out rather than throwing, so Parse still owns that
                // message — the collection it names is one the caller has already been cleared for.
                DenyUnreadableHops(meta.Name, path, graph, permissions);
                var rp = RelationPath.Parse(meta.Name, path, graph, metadata, opts.MaxRelationDepth);
                if (forSort && !rp.IsSortable)
                    throw new QueryException($"Sort across to-many relations is not supported: '{path}'.");
                return;
            }
            // "id" is always projected (PK); it is not in meta.Fields but is always valid.
            if (string.Equals(path, "id", StringComparison.OrdinalIgnoreCase)) return;
            if (!known.Contains(path))
                throw new QueryException($"Unknown field '{path}' on collection '{meta.Name}'.");
        }

        var conditionCount = 0;
        void Walk(FilterNode? node, int logicalDepth)
        {
            switch (node)
            {
                case null: return;
                case ComparisonFilter c:
                    conditionCount++;
                    if (conditionCount > opts.MaxFilterConditions)
                        throw new QueryException($"Too many filter conditions (max {opts.MaxFilterConditions}).");
                    CheckField(c.FieldPath);
                    break;
                case LogicalFilter l:
                    if (logicalDepth >= 2)
                        throw new QueryException(
                            "Nested logical groups are not supported; " +
                            "use a single level of _and/_or over field conditions.");
                    foreach (var child in l.Children) Walk(child, logicalDepth + 1);
                    break;
            }
        }

        Walk(q.Filter, 1);

        foreach (var s in q.Sort) CheckField(s.Field, forSort: true);
        if (q.Fields is not null) foreach (var f in q.Fields) CheckField(f, allowRelation: false);

        var limit = q.Limit <= 0 ? opts.DefaultLimit : Math.Min(q.Limit, opts.MaxLimit);
        var offset = Math.Max(0, q.Offset);

        return q with { Limit = limit, Offset = offset };
    }

    /// <summary>
    /// Walks a dotted path hop by hop, refusing the first whose target collection the caller cannot
    /// read. Stops at an unresolvable hop so <see cref="RelationPath.Parse"/> keeps ownership of that
    /// error — every collection reached before it is one the caller is cleared to know about.
    /// </summary>
    private static void DenyUnreadableHops(
        string rootCollection, string path, IRelationshipGraph graph, IPermissionService permissions)
    {
        var parts = path.Split('.');
        var current = rootCollection;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            var rel = graph.Resolve(current, parts[i]);
            if (rel is null) return;
            if (!permissions.CanRead(rel.TargetCollection))
                throw new PermissionDeniedException($"Read not permitted on '{rel.TargetCollection}'.");
            current = rel.TargetCollection;
        }
    }

    public static IReadOnlyList<string> SearchableFields(CollectionMetadata meta) =>
        meta.Fields.Where(f => f.Searchable && !f.Hidden).Select(f => f.Name).ToList();
}
