// src/Struo.Api/GraphQl/FilterInputTranslator.cs
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// Translates a submitted GraphQL filter input (read back as a nested dictionary because the
/// input type's RuntimeType is a dictionary) into the existing <see cref="FilterNode"/> tree,
/// so the whole query then flows through the existing QueryValidator + repository unchanged.
/// Cross-relation filtering: a key naming an M2O/O2M/M2M relation carries a nested filter dict,
/// which is flattened into dotted <see cref="ComparisonFilter"/> field paths (e.g. "category.name",
/// "category.parent.name") — unless that nested dict itself uses the reserved <c>some</c>/<c>none</c>
/// keys, which instead become a <see cref="RelationPredicateFilter"/> (ANY/NONE semantics), or the
/// reserved <c>junction</c> key, which becomes a dotted <c>_junction.&lt;field&gt;</c> path against
/// the relation's own junction row. <c>some</c>/<c>none</c>/<c>junction</c> are only meaningful
/// directly inside a relation's own filter dict — used at the top level of any filter (own-field
/// root) they throw a <see cref="QueryException"/>.
/// </summary>
public static class FilterInputTranslator
{
    public static readonly IReadOnlyDictionary<string, QueryOperator> OperatorTokens =
        new Dictionary<string, QueryOperator>(StringComparer.Ordinal)
        {
            ["eq"] = QueryOperator.Eq, ["neq"] = QueryOperator.Neq,
            ["in"] = QueryOperator.In, ["nin"] = QueryOperator.Nin,
            ["lt"] = QueryOperator.Lt, ["lte"] = QueryOperator.Lte,
            ["gt"] = QueryOperator.Gt, ["gte"] = QueryOperator.Gte,
            ["contains"] = QueryOperator.Contains,
            ["startsWith"] = QueryOperator.StartsWith,
            ["endsWith"] = QueryOperator.EndsWith,
        };

    public static string OperatorInputTypeName(FieldInterface iface, Type? clrType) => iface switch
    {
        FieldInterface.Number or FieldInterface.Slider or FieldInterface.Rating => NumberFilter(clrType),
        FieldInterface.Boolean or FieldInterface.Checkbox => "BooleanFilter",
        FieldInterface.DateTime or FieldInterface.Date => "DateTimeFilter",
        FieldInterface.File or FieldInterface.Image or FieldInterface.Uuid => "IdFilter",
        _ => "StringFilter"
    };

    private static string NumberFilter(Type? clrType)
    {
        var t = clrType is null ? null : Nullable.GetUnderlyingType(clrType) ?? clrType;
        return (t == typeof(int) || t == typeof(short) || t == typeof(byte) || t == typeof(long))
            ? "IntFilter" : "FloatFilter";
    }

    /// <summary>Flat-only translation (no cross-relation descent). Back-compatible entry point.</summary>
    public static FilterNode? Translate(IReadOnlyDictionary<string, object?>? filter)
        => Translate(filter, collection: "", relationTarget: null);

    /// <summary>
    /// Metadata-aware translation. <paramref name="relationTarget"/> returns the target collection
    /// name when a key is a filterable relation of <paramref name="collection"/>, else null; when it
    /// is null this behaves exactly like the flat overload.
    /// </summary>
    public static FilterNode? Translate(
        IReadOnlyDictionary<string, object?>? filter,
        string collection,
        Func<string, string, string?>? relationTarget)
        => TranslateCore(filter, collection, relationTarget, inRelation: false);

    // inRelation=true marks a call translating the INNER of a some/none predicate: that inner is
    // rooted at the relation's target collection and may use "junction" (-> "_junction.<f>", no
    // prefix — it is already rooted there) but may not itself carry a bare "some"/"none" (a further
    // quantifier must be expressed via a nested relation key instead, handled by TranslateRelation).
    private static FilterNode? TranslateCore(
        IReadOnlyDictionary<string, object?>? filter,
        string collection,
        Func<string, string, string?>? relationTarget,
        bool inRelation)
    {
        if (filter is null || filter.Count == 0) return null;
        var children = new List<FilterNode>();

        foreach (var (key, value) in filter)
        {
            if (value is null) continue;
            if (TryHandleGroup(children, key, value, collection, relationTarget, inRelation)) continue;
            if (TryHandleReservedKey(children, key, value, inRelation)) continue;

            if (relationTarget?.Invoke(collection, key) is { } target && AsDict(value) is { } nested)
                children.AddRange(TranslateRelation(key, nested, target, relationTarget));
            else
                AddField(children, key, value);
        }

        if (children.Count == 0) return null;
        return children.Count == 1 ? children[0] : new LogicalFilter(LogicalOperator.And, children);
    }

    private static bool TryHandleGroup(
        List<FilterNode> children, string key, object value, string collection,
        Func<string, string, string?>? relationTarget, bool inRelation)
    {
        if (string.Equals(key, "and", StringComparison.Ordinal))
        {
            AddGroup(children, value, LogicalOperator.And, collection, relationTarget, inRelation);
            return true;
        }
        if (string.Equals(key, "or", StringComparison.Ordinal))
        {
            AddGroup(children, value, LogicalOperator.Or, collection, relationTarget, inRelation);
            return true;
        }
        return false;
    }

    // Handles the reserved "some"/"none"/"junction" keys at whatever level TranslateCore is
    // currently walking. Outside a relation context (inRelation=false — i.e. at the root of any
    // Translate call, own-field or nested-list) all three are meaningless and throw. Inside a
    // relation's inner (inRelation=true) only "junction" is legal here — "some"/"none" belong to
    // TranslateRelation's own dispatch over the IMMEDIATE relation dict, not to a nested inner.
    private static bool TryHandleReservedKey(List<FilterNode> children, string key, object value, bool inRelation)
    {
        if (key is not ("some" or "none" or "junction")) return false;
        if (!inRelation)
            throw new QueryException($"'{key}' is only valid inside a relation filter.");
        if (key == "junction")
        {
            AddJunctionFields(children, value);
            return true;
        }
        throw new QueryException(
            $"'{key}' cannot be nested directly inside a relation predicate; express it as a nested relation filter instead.");
    }

    private static FilterNode Prefix(FilterNode node, string prefix) => node switch
    {
        ComparisonFilter c => c with { FieldPath = prefix + c.FieldPath },
        LogicalFilter l => new LogicalFilter(l.Op, l.Children.Select(ch => Prefix(ch, prefix)).ToList()),
        RelationPredicateFilter p => p with { RelationPath = prefix + p.RelationPath },
        _ => node
    };

    private static void AddGroup(
        List<FilterNode> into, object value, LogicalOperator op,
        string collection, Func<string, string, string?>? relationTarget, bool inRelation)
    {
        if (value is not System.Collections.IEnumerable list) return;
        var group = new List<FilterNode>();
        foreach (var item in list)
            if (AsDict(item) is { } d && TranslateCore(d, collection, relationTarget, inRelation) is { } node)
                group.Add(node);
        if (group.Count > 0) into.Add(new LogicalFilter(op, group));
    }

    // Translates the dict directly under a relation key (e.g. `tags: { ... }`): "some"/"none"
    // become RelationPredicateFilters rooted at `relation`; "junction" becomes dotted
    // "_junction.<f>" leaves prefixed with "<relation>."; every other key is a plain (possibly
    // further-nested-relation) field, collected and translated together at the end exactly like
    // before this feature existed, then prefixed the same way.
    private static List<FilterNode> TranslateRelation(
        string relation, IReadOnlyDictionary<string, object?> nested, string target,
        Func<string, string, string?>? relationTarget)
    {
        var children = new List<FilterNode>();
        Dictionary<string, object?>? rest = null;

        foreach (var (key, value) in nested)
        {
            if (value is null) continue;
            if (key is "some" or "none")
                children.Add(TranslateRelationPredicate(relation, key, value, target, relationTarget));
            else if (key == "junction")
                AddPrefixedJunctionFields(children, relation, value);
            else
                (rest ??= new Dictionary<string, object?>(StringComparer.Ordinal))[key] = value;
        }

        if (rest is { Count: > 0 } && TranslateCore(rest, target, relationTarget, inRelation: false) is { } restNode)
            children.Add(Prefix(restNode, relation + "."));

        return children;
    }

    private static FilterNode TranslateRelationPredicate(
        string relation, string key, object? value, string target,
        Func<string, string, string?>? relationTarget)
    {
        if (AsDict(value) is not { Count: > 0 } innerDict)
            throw new QueryException($"'{key}' on relation '{relation}' requires a non-empty filter object.");

        var inner = TranslateCore(innerDict, target, relationTarget, inRelation: true)
            ?? throw new QueryException($"'{key}' on relation '{relation}' requires a non-empty filter object.");
        var quantifier = key == "some" ? RelationQuantifier.Some : RelationQuantifier.None;
        return new RelationPredicateFilter(relation, quantifier, inner);
    }

    private static void AddPrefixedJunctionFields(List<FilterNode> into, string relation, object? value)
    {
        var temp = new List<FilterNode>();
        AddJunctionFields(temp, value);
        foreach (var node in temp)
            into.Add(Prefix(node, relation + "."));
    }

    // Expands a `junction: { <field>: { <op>: value } }` dict into "_junction.<field>" comparisons.
    private static void AddJunctionFields(List<FilterNode> into, object? value)
    {
        if (AsDict(value) is not { } fields) return;
        foreach (var (field, ops) in fields)
        {
            if (ops is null) continue;
            AddField(into, "_junction." + field, ops);
        }
    }

    private static void AddField(List<FilterNode> into, string field, object value)
    {
        if (AsDict(value) is not { } ops) return;
        foreach (var (token, opValue) in ops)
        {
            // HotChocolate reads the nested op-input (e.g. StringFilter) back as a dictionary
            // populated with EVERY declared field, not just the ones the client set — unset
            // operators default to null here. Skip them, or a single `{ eq: x }` filter would
            // explode into one ComparisonFilter per operator (neq/in/contains/... all value=null),
            // ANDed together and silently corrupting the query.
            if (opValue is null) continue;

            if (string.Equals(token, "isNull", StringComparison.Ordinal))
            {
                var isNull = opValue is true;
                into.Add(new ComparisonFilter(field, isNull ? QueryOperator.Null : QueryOperator.NNull, null));
            }
            else if (OperatorTokens.TryGetValue(token, out var qop))
            {
                into.Add(new ComparisonFilter(field, qop, opValue));
            }
        }
    }

    private static IReadOnlyDictionary<string, object?>? AsDict(object? o)
    {
        if (o is IReadOnlyDictionary<string, object?> ro) return ro;
        if (o is IDictionary<string, object?> d) return new Dictionary<string, object?>(d);
        return null;
    }
}
