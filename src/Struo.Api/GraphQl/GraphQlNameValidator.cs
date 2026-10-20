// src/Struo.Api/GraphQl/GraphQlNameValidator.cs
using Struo.Domain.Metadata;

namespace Struo.Api.GraphQl;

/// <summary>
/// The GraphQL type names and root field names one collection contributes to the schema.
/// </summary>
public sealed record CollectionSurface(
    string Collection,
    IReadOnlyList<string> TypeNames,
    IReadOnlyList<string> QueryFields,
    IReadOnlyList<string> MutationFields);

/// <summary>
/// Rejects collection sets whose generated GraphQL names collide, so the failure names the
/// collection(s) and the clashing name(s) instead of surfacing as a HotChocolate duplicate-type
/// error during schema build. GraphQL names are case-sensitive, matching HotChocolate's own
/// duplicate detection.
/// </summary>
public static class GraphQlNameValidator
{
    /// <summary>Type names the schema always contains, independent of any collection.</summary>
    public static readonly IReadOnlySet<string> FixedTypeNames = new HashSet<string>(StringComparer.Ordinal)
    {
        "Query", "Mutation",
        "String", "Int", "Float", "Boolean", "ID", "Long", "DateTime", "Date", "UUID", "Any",
        "TagItem", SchemaTypeMapper.TagItemInputName, "Translation", "DeletedFilter", "Revision",
        "StringFilter", "IntFilter", "FloatFilter", "DateTimeFilter", "BooleanFilter", "IdFilter",
        "AggregateInput", "FacetValue", "FacetResult",
    };

    /// <summary>Root field names the Query and Mutation types always contain.</summary>
    public static readonly IReadOnlySet<string> FixedRootFields =
        new HashSet<string>(StringComparer.Ordinal) { "_service" };

    public static void Validate(IReadOnlyList<CollectionSurface> surfaces)
    {
        var problems = new List<string>();
        problems.AddRange(Check("type", surfaces, s => s.TypeNames, FixedTypeNames));
        problems.AddRange(Check("Query field", surfaces, s => s.QueryFields, FixedRootFields));
        problems.AddRange(Check("Mutation field", surfaces, s => s.MutationFields, FixedRootFields));
        if (problems.Count == 0) return;

        throw new MetadataException(
            "GraphQL name collision: the collection set generates the same GraphQL name more than once. "
            + string.Join(" ", problems)
            + " Rename one of the collections so the generated names are unique.");
    }

    private static IEnumerable<string> Check(
        string kind, IReadOnlyList<CollectionSurface> surfaces,
        Func<CollectionSurface, IReadOnlyList<string>> namesOf, IReadOnlySet<string> fixedNames)
    {
        var owners = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var surface in surfaces)
            foreach (var name in namesOf(surface))
            {
                if (!owners.TryGetValue(name, out var list)) owners[name] = list = [];
                list.Add(surface.Collection);
            }

        foreach (var (name, collections) in owners.OrderBy(o => o.Key, StringComparer.Ordinal))
        {
            var reserved = fixedNames.Contains(name);
            if (!reserved && collections.Count == 1) continue;

            var who = string.Join(", ", collections.Distinct(StringComparer.Ordinal).Select(c => $"'{c}'"));
            if (reserved)
                yield return $"Collection {who} generates the {kind} name '{name}', which the CMS reserves.";
            else if (collections.Distinct(StringComparer.Ordinal).Count() == 1)
                yield return $"Collection {who} generates the {kind} name '{name}' more than once.";
            else
                yield return $"Collections {who} both generate the {kind} name '{name}'.";
        }
    }
}
