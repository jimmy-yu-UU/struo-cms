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
        var ctx = new ValidationContext(opts, graph, metadata, permissions);
        ctx.ValidateFilter(q.Filter, meta, hopsUsed: 0, logicalDepth: 1);

        foreach (var s in q.Sort) ctx.CheckField(s.Field, meta, forSort: true);
        if (q.Fields is not null) foreach (var f in q.Fields) ctx.CheckField(f, meta, allowRelation: false);
        var facets = ctx.ValidateFacets(q.Facets, meta);
        ctx.ValidateAggregate(q.Aggregate, meta);

        var limit = q.Limit <= 0 ? opts.DefaultLimit : Math.Min(q.Limit, opts.MaxLimit);
        var offset = Math.Max(0, q.Offset);

        return q with { Limit = limit, Offset = offset, Facets = facets };
    }

    public static IReadOnlyList<string> SearchableFields(CollectionMetadata meta) =>
        meta.Fields.Where(f => f.Searchable && !f.Hidden).Select(f => f.Name).ToList();

    /// <summary>
    /// Resolves each of a validated QueryModel's facet paths against the same collection metadata
    /// <see cref="Validate"/> already checked them under, without re-checking permissions. Used by
    /// ItemService after Validate has returned, once it needs the target rows' facet buckets computed.
    /// </summary>
    public static IReadOnlyList<ResolvedFacetPath> ResolveFacets(
        QueryModel q, CollectionMetadata meta, IRelationshipGraph graph, IMetadataProvider metadata) =>
        (q.Facets ?? []).Select(f => FacetPathResolver.Resolve(meta, f, graph, metadata)).ToList();

    /// <summary>
    /// Holds the per-request state (condition count, per-collection field allowlist cache) that used
    /// to live as closures over local functions in <see cref="Validate"/>. Recursion into a quantified
    /// relation predicate's inner filter needs that state to keep counting toward the same caps, so it
    /// is threaded through as fields here instead.
    /// </summary>
    private sealed class ValidationContext(
        StruoQueryOptions opts, IRelationshipGraph graph, IMetadataProvider metadata, IPermissionService permissions)
    {
        private int conditionCount;
        private readonly Dictionary<string, HashSet<string>> knownCache = new(StringComparer.OrdinalIgnoreCase);

        private static readonly IReadOnlySet<FieldInterface> Numeric =
            new HashSet<FieldInterface> { FieldInterface.Number, FieldInterface.Slider, FieldInterface.Rating };
        private static readonly IReadOnlySet<FieldInterface> Temporal =
            new HashSet<FieldInterface> { FieldInterface.Date, FieldInterface.DateTime };

        /// <summary>
        /// Validates and deduplicates (Ordinal, preserving first occurrence) the requested facet
        /// paths, returning the deduplicated list to be stored back on the returned QueryModel. Each
        /// path's relation target (if any) is permission-checked BEFORE its shape is resolved — same
        /// reason as <see cref="DenyUnreadableHops"/>: an unresolvable path on an unreadable
        /// collection must fail the same way a resolvable one would, so a caller cannot use the error
        /// to learn whether a hidden field/relation exists.
        /// </summary>
        public IReadOnlyList<string>? ValidateFacets(IReadOnlyList<string>? facets, CollectionMetadata meta)
        {
            if (facets is null) return null;
            var distinct = facets.Distinct(StringComparer.Ordinal).ToList();
            if (distinct.Count > opts.MaxFacets) throw new QueryException($"Too many facets (max {opts.MaxFacets}).");
            foreach (var raw in distinct)
            {
                if (string.IsNullOrWhiteSpace(raw)) throw new QueryException("Facet path must not be empty.");
                DenyUnreadableFacetTarget(meta, raw);
                FacetPathResolver.Resolve(meta, raw, graph, metadata);
            }
            return distinct;
        }

        // Only the head segment can name a relation (facet paths cap at one hop), so unlike
        // DenyUnreadableHops this checks a single segment rather than walking a chain.
        private void DenyUnreadableFacetTarget(CollectionMetadata meta, string raw)
        {
            var head = raw.Split('.')[0];
            if (meta.Fields.Any(f => string.Equals(f.Name, head, StringComparison.OrdinalIgnoreCase))) return;
            var rel = graph.Resolve(meta.Name, head)
                ?? meta.Relations.FirstOrDefault(r => string.Equals(r.ForeignKey, head, StringComparison.OrdinalIgnoreCase));
            if (rel is null) return;
            if (!permissions.CanRead(rel.TargetCollection))
                throw new PermissionDeniedException($"Read not permitted on '{rel.TargetCollection}'.");
        }

        public void ValidateAggregate(AggregateSpec? spec, CollectionMetadata meta)
        {
            if (spec is null) return;
            var total = spec.Fields.Sum(kv => kv.Value.Count);
            if (total > opts.MaxAggregates) throw new QueryException($"Too many aggregate fields (max {opts.MaxAggregates}).");
            foreach (var (op, fields) in spec.Fields)
                foreach (var field in fields) ValidateAggregateField(op, field, meta);
        }

        private void ValidateAggregateField(AggregateOp op, string field, CollectionMetadata meta)
        {
            if (!Known(meta).Contains(field))
                throw new QueryException($"Unknown field '{field}' on collection '{meta.Name}'.");
            if (op == AggregateOp.Count) return;
            var fm = meta.Fields.FirstOrDefault(f => string.Equals(f.Name, field, StringComparison.OrdinalIgnoreCase));
            var iface = fm?.Interface ?? FieldInterface.Uuid;
            var ok = op switch
            {
                AggregateOp.Sum or AggregateOp.Avg => Numeric.Contains(iface),
                AggregateOp.Min or AggregateOp.Max => Numeric.Contains(iface) || Temporal.Contains(iface),
                _ => false,
            };
            if (!ok) throw new QueryException($"Aggregate '{op.ToString().ToLowerInvariant()}' is not supported on field '{field}' ({iface}).");
        }

        // predicateRelation: the enclosing RelationPredicateFilter's OWN last hop relation, threaded
        // through so a "_junction.<field>" leaf reached DIRECTLY in that predicate's inner (see
        // ValidateJunctionLeaf) can be validated against it — a predicate's inner has already consumed
        // every relation hop leading to `meta`, so "_junction" there can never be resolved by walking
        // hops from `meta` the way an ordinary dotted field path (e.g. "labels._junction.note") is.
        // Null outside any predicate's inner (the top-level call, and every non-predicate recursion).
        public void ValidateFilter(
            FilterNode? node, CollectionMetadata meta, int hopsUsed, int logicalDepth,
            RelationMetadata? predicateRelation = null)
        {
            switch (node)
            {
                case null:
                    return;
                case ComparisonFilter c:
                    CountCondition();
                    if (IsJunctionLeaf(c.FieldPath)) ValidateJunctionLeaf(c.FieldPath, predicateRelation);
                    else CheckField(c.FieldPath, meta, hopsUsed: hopsUsed);
                    return;
                case LogicalFilter l:
                    if (logicalDepth >= 2)
                        throw new QueryException(
                            "Nested logical groups are not supported; " +
                            "use a single level of _and/_or over field conditions.");
                    foreach (var child in l.Children)
                        ValidateFilter(child, meta, hopsUsed, logicalDepth + 1, predicateRelation);
                    return;
                case RelationPredicateFilter p:
                    ValidatePredicate(p, meta, hopsUsed);
                    return;
                default:
                    throw new QueryException($"Unsupported filter node '{node.GetType().Name}'.");
            }
        }

        private static bool IsJunctionLeaf(string fieldPath) =>
            fieldPath.StartsWith(FilterReservedTokens.Junction + ".", StringComparison.Ordinal);

        private void ValidatePredicate(RelationPredicateFilter p, CollectionMetadata meta, int hopsUsed)
        {
            // Same permission gate as a dotted field path: every collection the predicate's relation
            // prefix traverses needs its own read grant, checked before the prefix is even parsed (a
            // denied caller must not learn whether an unresolvable relation name would have failed
            // for a different reason). The prefix has no leaf, so the walk is given a trailing "."
            // to make its (parts.Length - 1)-hop loop cover every segment.
            DenyUnreadableHops(meta.Name, p.RelationPath + ".");
            var rp = RelationPath.ParseRelationOnly(
                meta.Name, p.RelationPath, graph, metadata, opts.MaxRelationDepth - hopsUsed, opts.MaxRelationDepth);
            var target = metadata.GetCollection(rp.TerminalCollection)
                ?? throw new QueryException($"Unknown collection '{rp.TerminalCollection}'.");
            ValidateFilter(p.Inner, target, hopsUsed + rp.Segments.Count, logicalDepth: 1, rp.Segments[^1].Relation);
        }

        // Validates a "_junction.<field>" leaf reached directly in a predicate's inner filter (see the
        // ValidateFilter/ValidatePredicate doc comments). Read-grant on the junction collection is
        // checked BEFORE the field name is looked up — resolving an unknown field first would let a
        // caller without read on the junction collection distinguish a real payload field from an
        // invented one by response code (400 vs 403), the same oracle DenyUnreadableHops exists to
        // prevent for ordinary hops.
        private void ValidateJunctionLeaf(string fieldPath, RelationMetadata? predicateRelation)
        {
            if (predicateRelation is null)
                throw new QueryException($"'_junction' is only valid inside a relation predicate's inner filter: '{fieldPath}'.");
            if (predicateRelation.Kind != RelationKind.ManyToMany || predicateRelation.JunctionCollection is null)
                throw new QueryException($"'{fieldPath}': '_junction' is only valid after a many-to-many relation with a junction collection.");

            var junctionCollection = predicateRelation.JunctionCollection;
            if (!permissions.CanRead(junctionCollection))
                throw new PermissionDeniedException($"Read not permitted on '{junctionCollection}'.");

            var remainder = fieldPath[(FilterReservedTokens.Junction.Length + 1)..];
            if (remainder.Length == 0 || remainder.Contains('.'))
                throw new QueryException($"'{fieldPath}': '_junction' must be followed by exactly one junction field.");

            var junctionMeta = metadata.GetCollection(junctionCollection)
                ?? throw new QueryException($"Unknown collection '{junctionCollection}' in path '{fieldPath}'.");
            var known = junctionMeta.Fields.Any(f => !f.Hidden && string.Equals(f.Name, remainder, StringComparison.OrdinalIgnoreCase));
            if (!known)
                throw new QueryException($"Unknown field '{remainder}' on collection '{junctionCollection}' in path '{fieldPath}'.");
        }

        public void CheckField(
            string path, CollectionMetadata meta, bool forSort = false, bool allowRelation = true, int hopsUsed = 0)
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
                // A "_junction.<field>" segment is handled inside the same walk (see
                // DenyUnreadableHops): checking the junction collection's read grant only after Parse
                // returns would let a caller without read on the junction collection distinguish a
                // real payload field from an invented one by response code (400 vs 403) — the same
                // oracle this walk exists to prevent for ordinary hops.
                DenyUnreadableHops(meta.Name, path);
                var rp = RelationPath.Parse(
                    meta.Name, path, graph, metadata, opts.MaxRelationDepth - hopsUsed, opts.MaxRelationDepth);
                if (forSort && !rp.IsSortable)
                    throw new QueryException($"Sort across to-many relations is not supported: '{path}'.");
                return;
            }
            // "id" is always projected (PK); it is not in meta.Fields but is always valid.
            if (string.Equals(path, "id", StringComparison.OrdinalIgnoreCase)) return;
            if (!Known(meta).Contains(path))
                throw new QueryException($"Unknown field '{path}' on collection '{meta.Name}'.");
        }

        // Allowlist: a collection's own fields, plus its declared many-to-one relation foreign keys
        // (e.g. "categoryId" on article) so callers (incl. the frontend RelatedList) can filter/sort
        // by the FK column even though it carries no [CmsField]. Never widened to arbitrary
        // non-relation columns.
        //
        // Hidden fields are excluded on purpose: they hold credentials (User.Password /
        // User.AccessToken) that projection already refuses to serialize. If they stayed
        // filterable/sortable, meta.total would become a blind-extraction oracle
        // (?filter[password][_startsWith]=...) that leaks the value one character at a time.
        //
        // Computed lazily per collection and cached: a quantified predicate's inner filter runs
        // against the target collection's metadata, which differs from the root's on every recursion.
        private HashSet<string> Known(CollectionMetadata meta)
        {
            if (knownCache.TryGetValue(meta.Name, out var cached)) return cached;
            var known = meta.Fields.Where(f => !f.Hidden).Select(f => f.Name)
                .Concat(meta.Relations
                    .Where(r => r.Kind == RelationKind.ManyToOne && r.ForeignKey is not null)
                    .Select(r => r.ForeignKey!))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            knownCache[meta.Name] = known;
            return known;
        }

        private void CountCondition()
        {
            conditionCount++;
            if (conditionCount > opts.MaxFilterConditions)
                throw new QueryException($"Too many filter conditions (max {opts.MaxFilterConditions}).");
        }

        /// <summary>
        /// Walks a dotted path hop by hop, refusing the first whose target collection the caller
        /// cannot read. Stops at an unresolvable hop so <see cref="RelationPath.Parse"/> or
        /// <see cref="RelationPath.ParseRelationOnly"/> keeps ownership of that error — every
        /// collection reached before it is one the caller is cleared to know about.
        /// <para>
        /// A <c>_junction</c> segment is not itself a relation hop — it names the M2M relation
        /// resolved one segment earlier's junction collection. Its read grant is checked here, against
        /// that relation's <c>JunctionCollection</c>, before the walk returns; checking it only after
        /// <see cref="RelationPath.Parse"/> resolves the leaf would let an unreadable-junction caller
        /// distinguish a real payload field from an invented one by response code.
        /// </para>
        /// </summary>
        private void DenyUnreadableHops(string rootCollection, string path)
        {
            var parts = path.Split('.');
            var current = rootCollection;
            RelationMetadata? previousRel = null;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i] == FilterReservedTokens.Junction)
                {
                    if (previousRel?.JunctionCollection is null) return; // misuse: Parse owns the error
                    if (!permissions.CanRead(previousRel.JunctionCollection))
                        throw new PermissionDeniedException($"Read not permitted on '{previousRel.JunctionCollection}'.");
                    return; // the one field after "_junction" is the leaf; no further hop to check
                }

                var rel = graph.Resolve(current, parts[i]);
                if (rel is null) return;
                if (!permissions.CanRead(rel.TargetCollection))
                    throw new PermissionDeniedException($"Read not permitted on '{rel.TargetCollection}'.");
                current = rel.TargetCollection;
                previousRel = rel;
            }
        }
    }
}
