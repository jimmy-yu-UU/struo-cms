// src/Struo.Api/GraphQl/CollectionResolvers.cs
using HotChocolate.Execution;
using HotChocolate.Execution.Processing;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

internal static class CollectionResolvers
{
    internal static ObjectFieldConfiguration SingleField(string collection, IMetadataProvider metadata)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.SingleFieldName(collection), null,
            TypeReference.Parse(SchemaTypeMapper.TypeName(collection)),
            resolver: ctx => ResolveSingle(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration("id", null, TypeReference.Parse("ID!")));
        config.Arguments.Add(new ArgumentConfiguration("locale", null, TypeReference.Parse("String")));
        return config;
    }

    internal static ObjectFieldConfiguration ListField(string collection, IMetadataProvider metadata)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.ListFieldName(collection), null,
            TypeReference.Parse(SchemaTypeMapper.TypeName(collection) + "List!"),
            resolver: ctx => ResolveList(ctx, collection, metadata));
        config.Arguments.Add(new ArgumentConfiguration("filter", null, TypeReference.Parse(SchemaTypeMapper.TypeName(collection) + "FilterInput")));
        config.Arguments.Add(new ArgumentConfiguration("sort", null, TypeReference.Parse("[String!]")));
        config.Arguments.Add(new ArgumentConfiguration("limit", null, TypeReference.Parse("Int")));
        config.Arguments.Add(new ArgumentConfiguration("offset", null, TypeReference.Parse("Int")));
        config.Arguments.Add(new ArgumentConfiguration("search", null, TypeReference.Parse("String")));
        config.Arguments.Add(new ArgumentConfiguration("locale", null, TypeReference.Parse("String")));
        config.Arguments.Add(new ArgumentConfiguration("deleted", null, TypeReference.Parse("DeletedFilter")));
        return config;
    }

    private static async ValueTask<object?> ResolveSingle(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        var locale = ctx.ArgumentValue<string?>("locale");
        var metadata = ctx.Service<IMetadataProvider>();
        var deep = SelectionDeepSpec(ctx, collection, metadata, elementIsDirect: true);
        var data = await ctx.Service<IGraphQlDataSource>().GetAsync(collection, id, deep, locale, ctx.RequestAborted);
        return data;
    }

    private static async ValueTask<object?> ResolveList(IResolverContext ctx, string collection, IMetadataProvider metadata)
    {
        var filter = ctx.ArgumentValue<IReadOnlyDictionary<string, object?>?>("filter");
        var sort = ctx.ArgumentValue<IReadOnlyList<string>?>("sort");
        var limit = ctx.ArgumentValue<int?>("limit");
        var offset = ctx.ArgumentValue<int?>("offset");
        var search = ctx.ArgumentValue<string?>("search");
        var locale = ctx.ArgumentValue<string?>("locale");
        var deleted = ctx.ArgumentValue<DeletedFilter?>("deleted") ?? DeletedFilter.Exclude;
        if (deleted != DeletedFilter.Exclude && !ctx.Service<IPermissionService>().CanDelete(collection))
            throw new PermissionDeniedException("Viewing deleted items requires delete permission.");
        var deep = SelectionDeepSpec(ctx, collection, metadata, elementIsDirect: false);
        var query = GraphQlQueryBuilder.BuildQuery(
            filter, sort, limit, offset, search, deep,
            collection, RelationTargets(metadata));
        var page = await ctx.Service<IGraphQlDataSource>().QueryAsync(collection, query, locale, deleted, ctx.RequestAborted);
        return new PagedResultView(page.Data.Cast<object>().ToList(), page.Total);
    }

    /// <summary>
    /// Delegate for FilterInputTranslator: returns the target collection name when <paramref name="key"/>
    /// is a filterable relation of <paramref name="coll"/> (so a nested filter descends into a dotted
    /// path), else null. M2O participates only with a foreign key (its engine hop dereferences it);
    /// O2M and M2M participate unconditionally (they resolve via the reverse FK / junction) — to-many
    /// paths carry ANY/EXISTS semantics.
    /// </summary>
    private static Func<string, string, string?> RelationTargets(IMetadataProvider metadata) =>
        (coll, key) => metadata.GetCollection(coll)?.Relations
            .FirstOrDefault(r => string.Equals(r.Name, key, StringComparison.OrdinalIgnoreCase)
                && ((r.Kind == RelationKind.ManyToOne && r.ForeignKey is not null)
                    || r.Kind is RelationKind.OneToMany or RelationKind.ManyToMany))
            ?.TargetCollection;

    /// <summary>
    /// Builds a nested <see cref="DeepSpec"/> from the client's selection tree: each selected field
    /// that is a relation of the collection contributes a <see cref="DeepRelationSpec"/> whose nested
    /// <c>Deep</c> is built recursively from that relation's own sub-selection. Depth is bounded by
    /// ItemService's MaxRelationDepth validation and HotChocolate's max-execution-depth.
    /// </summary>
    internal static DeepSpec? SelectionDeepSpec(
        IResolverContext ctx, string collection, IMetadataProvider metadata, bool elementIsDirect)
    {
        ObjectType elementType;
        SelectionEnumerator childSelections;
        if (elementIsDirect)
        {
            elementType = (ObjectType)ctx.Selection.Field.Type.NamedType();
            childSelections = ctx.GetSelections(elementType);
        }
        else
        {
            var listType = (ObjectType)ctx.Selection.Field.Type.NamedType();          // XList
            var itemsSel = ctx.GetSelections(listType).FirstOrDefault(s => s.Field.Name == "items");
            if (itemsSel is null) return null;
            elementType = (ObjectType)itemsSel.Field.Type.NamedType();                  // X
            childSelections = ctx.GetSelections(elementType, itemsSel);
        }
        return BuildDeep(ctx, collection, childSelections, metadata);
    }

    private static DeepSpec? BuildDeep(
        IResolverContext ctx, string collection, SelectionEnumerator childSelections, IMetadataProvider metadata)
    {
        var relByName = metadata.GetCollection(collection)?.Relations
            .ToDictionary(r => r.Name, r => r, StringComparer.OrdinalIgnoreCase);
        if (relByName is null || relByName.Count == 0) return null;

        var map = new Dictionary<string, DeepRelationSpec>(StringComparer.OrdinalIgnoreCase);
        foreach (var sel in childSelections)
        {
            if (!relByName.TryGetValue(sel.Field.Name, out var rel)) continue;
            if (map.ContainsKey(rel.Name)) continue; // aliased-duplicate selections: first wins per level
            var targetType = (ObjectType)sel.Field.Type.NamedType();
            var nested = BuildDeep(ctx, rel.TargetCollection, ctx.GetSelections(targetType, sel), metadata);

            // To-many list fields may carry filter/sort/limit/offset arguments (8c.3b). M2O has no
            // args declared on its field (CollectionSchemaBuilder), so this only ever fires for
            // OneToMany/ManyToMany relations.
            FilterNode? filter = null;
            IReadOnlyList<SortField>? sort = null;
            int? limit = null, offset = null;
            if (rel.Kind is RelationKind.OneToMany or RelationKind.ManyToMany)
            {
                var args = ReadSelectionArgs(ctx, sel);
                if (args.TryGetValue("filter", out var fv) && fv is IReadOnlyDictionary<string, object?> fd)
                    filter = FilterInputTranslator.Translate(fd, rel.TargetCollection, RelationTargets(metadata));
                if (args.TryGetValue("sort", out var sv) && sv is IEnumerable<object?> st)
                    sort = GraphQlQueryBuilder.ParseSort(st.Select(x => x?.ToString() ?? "").ToList());
                if (args.TryGetValue("limit", out var lv) && lv is not null)
                    limit = Convert.ToInt32(lv);
                if (args.TryGetValue("offset", out var ov) && ov is not null)
                    offset = Convert.ToInt32(ov);
            }

            map[rel.Name] = new DeepRelationSpec(null, limit, nested)
            {
                Filter = filter,
                Sort = sort,
                Offset = offset
            };
        }
        return map.Count == 0 ? null : new DeepSpec(map);
    }

    // HotChocolate's InputParser is stateless/reusable (mirrors how the runtime itself owns one
    // per schema); used to turn a selection's raw argument literal into a runtime value below.
    private static readonly InputParser InputParser = new();

    /// <summary>
    /// Reads a CHILD selection's own argument values (not the current resolver's args — HotChocolate
    /// only exposes those via <c>ctx.ArgumentValue&lt;T&gt;</c>, which is scoped to the selection
    /// <paramref name="ctx"/> is currently resolving). <see cref="Selection.Arguments"/>
    /// (<c>ArgumentMap : IReadOnlyDictionary&lt;string, ArgumentValue&gt;</c>) never reports
    /// <see cref="ArgumentValue.IsFullyCoerced"/> for a child selection BuildDeep is walking ahead
    /// of execution — HotChocolate defers that coercion until right before the selection's own
    /// resolver runs, regardless of whether its literal is inline or a <c>$variable</c> reference.
    /// So both shapes are handled uniformly via <see cref="ArgumentValue.ValueLiteral"/>: a plain
    /// literal (e.g. an <c>ObjectValueNode</c>) is used as-is; a <c>VariableNode</c> is substituted
    /// with the variable's own literal off <c>ctx.Variables</c> (<see cref="IVariableValueCollection"/>
    /// — its <c>GetValue&lt;T&gt;</c> is constrained to <c>T : IValueNode</c>, i.e. it only ever
    /// hands back syntax, never a pre-coerced runtime object). Either way the resulting literal is
    /// then parsed into a runtime value via <see cref="HotChocolate.Types.InputParser.ParseLiteral"/>
    /// against the argument's own declared <see cref="ArgumentValue.Type"/> — exactly how
    /// HotChocolate parses any other literal argument. Isolated here (8b.1's <c>SentFieldsOnly</c>
    /// precedent) so <see cref="BuildDeep"/> stays free of HC-internals detail.
    /// </summary>
    private static IReadOnlyDictionary<string, object?> ReadSelectionArgs(IResolverContext ctx, Selection sel)
    {
        var result = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, argValue) in sel.Arguments)
        {
            // A CHILD selection (one BuildDeep is walking ahead of execution, not the selection
            // ctx is currently resolving) is never reported IsFullyCoerced here regardless of
            // whether its literal is inline or a $variable reference — HotChocolate only performs
            // that coercion lazily, right before the selection's own resolver runs. So both shapes
            // go through the same path: resolve the effective IValueNode (substituting a
            // VariableNode with the variable's own literal off ctx.Variables), then parse it
            // against the argument's declared input type exactly like HotChocolate parses any
            // other literal.
            var literal = argValue.ValueLiteral;
            if (literal is VariableNode variableNode)
                literal = ctx.Variables.GetValue<IValueNode>(variableNode.Name.Value);
            if (literal is null or NullValueNode) continue; // argument not supplied
            result[name] = InputParser.ParseLiteral(literal, argValue.Type, HotChocolate.Path.Root);
        }
        return result;
    }
}
