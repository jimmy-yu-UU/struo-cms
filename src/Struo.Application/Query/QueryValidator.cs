// src/Struo.Application/Query/QueryValidator.cs
using Struo.Application.Configuration;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

public static class QueryValidator
{
    public static QueryModel Validate(QueryModel q, CollectionMetadata meta, StruoQueryOptions opts)
    {
        var known = meta.Fields.Select(f => f.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);

        void CheckField(string path)
        {
            if (path.Contains('.'))
                throw new QueryException($"Relation path '{path}' is not supported until Phase 3.");
            if (!known.Contains(path))
                throw new QueryException($"Unknown field '{path}' on collection '{meta.Name}'.");
        }

        var conditionCount = 0;
        void Walk(FilterNode? node)
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
                    foreach (var child in l.Children) Walk(child);
                    break;
            }
        }

        Walk(q.Filter);

        foreach (var s in q.Sort) CheckField(s.Field);
        if (q.Fields is not null) foreach (var f in q.Fields) CheckField(f);

        var limit = q.Limit <= 0 ? opts.DefaultLimit : Math.Min(q.Limit, opts.MaxLimit);
        var offset = Math.Max(0, q.Offset);

        return q with { Limit = limit, Offset = offset };
    }

    public static IReadOnlyList<string> SearchableFields(CollectionMetadata meta) =>
        meta.Fields.Where(f => f.Searchable).Select(f => f.Name).ToList();
}
