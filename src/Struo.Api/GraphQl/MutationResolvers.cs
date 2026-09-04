// src/Struo.Api/GraphQl/MutationResolvers.cs
using HotChocolate.Language;
using HotChocolate.Resolvers;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Descriptors.Configurations;
using Struo.Application.Metadata;
using Struo.Domain.Query;

namespace Struo.Api.GraphQl;

/// <summary>
/// Root mutation field configs + resolvers, one create/update/delete per collection. Mirrors
/// <see cref="CollectionResolvers"/> (queries). Resolvers convert the typed input to a JsonElement
/// and delegate to <see cref="IGraphQlDataSource"/> (which wraps ItemService); create/update re-read
/// the row so the returned node matches a query result.
/// </summary>
internal static class MutationResolvers
{
    internal static ObjectFieldConfiguration CreateField(string collection, IMetadataProvider metadata)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.CreateFieldName(collection), null,
            TypeReference.Parse(SchemaTypeMapper.TypeName(collection)),
            resolver: ctx => ResolveCreate(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration(
            "input", null, TypeReference.Parse(SchemaTypeMapper.CreateInputName(collection) + "!")));
        config.Arguments.Add(new ArgumentConfiguration("locale", null, TypeReference.Parse("String")));
        return config;
    }

    private static async ValueTask<object?> ResolveCreate(IResolverContext ctx, string collection)
    {
        var input = ctx.ArgumentValue<IReadOnlyDictionary<string, object?>?>("input");
        var locale = ctx.ArgumentValue<string?>("locale");
        var payloadRelations = PayloadRelations(ctx, collection);
        var body = MutationInputMapper.ToJsonElement(
            FoldLinks(FoldTranslations(SentFieldsOnly(ctx, "input", input)), payloadRelations));

        var source = ctx.Service<IGraphQlDataSource>();
        var created = await source.CreateAsync(collection, body, ctx.RequestAborted);

        // Re-read so the returned node matches a query result (relations/translations resolvable).
        var id = created.GetValueOrDefault("id")?.ToString();
        if (id is null) return created;
        var deep = CollectionResolvers.SelectionDeepSpec(
            ctx, collection, ctx.Service<IMetadataProvider>(), elementIsDirect: true);

        // Best-effort re-read: the write already committed, so a denied/absent re-read must not
        // surface as FORBIDDEN and hide a successful create (that would invite duplicate-create
        // retries from the caller). Fall back to the write result rather than the re-read.
        try
        {
            return await source.GetAsync(collection, id, deep, locale, ctx.RequestAborted) ?? created;
        }
        catch (PermissionDeniedException)
        {
            return created;
        }
    }

    internal static ObjectFieldConfiguration UpdateField(string collection, IMetadataProvider metadata)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.UpdateFieldName(collection), null,
            TypeReference.Parse(SchemaTypeMapper.TypeName(collection)),
            resolver: ctx => ResolveUpdate(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration("id", null, TypeReference.Parse("ID!")));
        config.Arguments.Add(new ArgumentConfiguration(
            "input", null, TypeReference.Parse(SchemaTypeMapper.UpdateInputName(collection) + "!")));
        config.Arguments.Add(new ArgumentConfiguration("locale", null, TypeReference.Parse("String")));
        return config;
    }

    private static async ValueTask<object?> ResolveUpdate(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        var input = ctx.ArgumentValue<IReadOnlyDictionary<string, object?>?>("input");
        var locale = ctx.ArgumentValue<string?>("locale");
        var payloadRelations = PayloadRelations(ctx, collection);
        var body = MutationInputMapper.ToJsonElement(
            FoldLinks(FoldTranslations(SentFieldsOnly(ctx, "input", input)), payloadRelations));

        var source = ctx.Service<IGraphQlDataSource>();
        var updated = await source.UpdateAsync(collection, id, body, ctx.RequestAborted);
        if (updated is null) return null; // unknown id -> null (REST 404 parity)

        var deep = CollectionResolvers.SelectionDeepSpec(
            ctx, collection, ctx.Service<IMetadataProvider>(), elementIsDirect: true);

        // Best-effort re-read: the write already committed, so a denied/absent re-read must not
        // surface as FORBIDDEN and hide a successful update. Fall back to the write result.
        try
        {
            return await source.GetAsync(collection, id, deep, locale, ctx.RequestAborted) ?? updated;
        }
        catch (PermissionDeniedException)
        {
            return updated;
        }
    }

    // HotChocolate's coerced argument dictionary (ctx.ArgumentValue<IReadOnlyDictionary<string,object?>?>)
    // always contains EVERY declared input field for a Dictionary-runtime-type InputObjectType — an
    // optional field the client never sent is backfilled with null rather than omitted. That breaks
    // update's partial merge (ItemService.UpdateAsync treats "key present" as "client sent it" — see
    // bodyKeys in ItemService.UpdateAsync — so a backfilled null would silently overwrite a field the
    // client never touched) AND create's defaults (ItemService.CreateAsync deserializes the body onto
    // a fresh entity, so a backfilled null overwrites a field's CLR default, e.g. Article.Status, and
    // can 500 on a non-nullable value-type scalar the client omitted). Both resolvers route the
    // coerced dict through this helper. The request's argument LITERAL (post-variable-substitution)
    // still reflects only the client-supplied keys, so intersect the coerced dict's values against the
    // literal's field names to recover the true sent-fields set without needing to re-derive values
    // from the literal ourselves.
    //
    // HotChocolate v16 backfills every declared field of a dict-runtime InputObjectType when the
    // client omits it (see above) — NESTED input objects (TagItemInput / XFieldItemInput items) are
    // ALSO dict-runtime and backfilled, so a Repeater sub-field the client omitted would reach the
    // body as null (and, for a non-nullable value-type sub-field, 500 on the child POCO deserialize).
    // This prunes recursively: keep only keys present in the argument literal at every depth. Any
    // (Json/KeyValue) is safe under this blind structural recursion — it is never backfilled, so its
    // literal keys always equal its value keys and recursing into it is a no-op.
    private static IReadOnlyDictionary<string, object?>? SentFieldsOnly(
        IResolverContext ctx, string argumentName, IReadOnlyDictionary<string, object?>? coerced)
    {
        if (coerced is null) return null;
        var literal = ctx.ArgumentLiteral<IValueNode>(argumentName);
        return PruneObject(coerced, literal) as IReadOnlyDictionary<string, object?> ?? coerced;
    }

    // M2M relations for `collection` that carry junction payload — drives FoldLinks below. Reuses
    // CollectionSchemaBuilder.HasExposablePayload, the EXACT predicate that decided which
    // relations got a `<rel>Links` input field in the first place (not the raw HasPayload flag —
    // a relation whose only payload field is hidden/unmappable never got a `<rel>Links` field at
    // all, so it must never be treated as one here either).
    private static IReadOnlyList<M2MDescriptor> PayloadRelations(IResolverContext ctx, string collection)
    {
        var metadataProvider = ctx.Service<IMetadataProvider>();
        return ctx.Service<IM2MDescriptorSource>().M2MDescriptors(collection)
            .Where(d => CollectionSchemaBuilder.HasExposablePayload(d, metadataProvider))
            .ToList();
    }

    // GraphQL sends `<rel>Links` as a list of { id, ...payload } entries — already the exact REST
    // mixed-array shape ItemService/SyncM2MAsync accepts. Fold it into `<rel>` (inserting or
    // replacing) and drop the `<rel>Links` key, so the REST body carries the junction payload
    // through the existing M2M sync path. When both `<rel>` and `<rel>Links` are sent, `<rel>Links`
    // wins (its value is written into `<rel>` after the plain array is copied over). This applies
    // even to an EXPLICIT `<rel>Links: null` — the key is still present (ContainsKey is true
    // regardless of the value), so it still overwrites/discards a simultaneously-sent `<rel>`
    // array rather than leaving it alone. Runs on the already-pruned dict (after
    // SentFieldsOnly/FoldTranslations), returning a NEW dict so the input is never mutated (CLAUDE
    // immutability) — a no-op (same reference back) when no `<rel>Links` key is present for any
    // payload relation.
    private static IReadOnlyDictionary<string, object?>? FoldLinks(
        IReadOnlyDictionary<string, object?>? input, IReadOnlyList<M2MDescriptor> payloadRelations)
    {
        if (input is null || payloadRelations.Count == 0) return input;

        Dictionary<string, object?>? result = null;
        foreach (var rel in payloadRelations)
        {
            var linksKey = SchemaTypeMapper.LinksFieldName(rel.RelationName);
            if (!input.ContainsKey(linksKey)) continue;

            result ??= new Dictionary<string, object?>(input);
            result[rel.RelationName] = result[linksKey];
            result.Remove(linksKey);
        }
        return result ?? input;
    }

    // GraphQL sends `translations` as a list of { locale, fields } entries (mirroring the read side's
    // [Translation!]); ItemService.SyncTranslationsAsync consumes a locale-keyed object
    // { "<locale>": { <field>: <value> } }. Fold the (already SentFieldsOnly-pruned) list into that
    // object, returning a NEW dict so the input is not mutated (CLAUDE immutability). Runs AFTER the
    // recursive prune (so each entry's `fields` carries only client-sent sub-fields) and BEFORE
    // ToJsonElement. Absent/unexpected `translations` -> pass through unchanged (ItemService then
    // enforces create-requires-default-locale / rejects a non-object). Duplicate locale -> last wins.
    private static IReadOnlyDictionary<string, object?>? FoldTranslations(
        IReadOnlyDictionary<string, object?>? input)
    {
        if (input is null) return null;
        var raw = input.GetValueOrDefault("translations");
        if (raw is not System.Collections.IEnumerable entries || raw is string) return input;

        var byLocale = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var entry in entries)
        {
            if (entry is not IReadOnlyDictionary<string, object?> e) continue;
            if (e.GetValueOrDefault("locale") is not string locale) continue;
            byLocale[locale] = e.GetValueOrDefault("fields");
        }

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (k, v) in input) result[k] = v;
        result["translations"] = byLocale;
        return result;
    }

    // Returns a new value graph containing only the keys/elements present in the literal. When the
    // literal shape does not match the value (e.g. a $variable whose literal is not fully expanded, or
    // a scalar), the value is returned as-is.
    private static object? PruneValue(object? value, IValueNode? literal) => (value, literal) switch
    {
        (IReadOnlyDictionary<string, object?> dict, ObjectValueNode obj) => PruneObject(dict, obj),
        (System.Collections.IEnumerable list and not string, ListValueNode listNode) => PruneList(list, listNode),
        _ => value,
    };

    private static Dictionary<string, object?> PruneObject(
        IReadOnlyDictionary<string, object?> dict, IValueNode? literal)
    {
        if (literal is not ObjectValueNode obj) return new Dictionary<string, object?>(dict);
        var byName = obj.Fields.ToDictionary(f => f.Name.Value, f => f.Value, StringComparer.Ordinal);
        var result = new Dictionary<string, object?>(byName.Count, StringComparer.Ordinal);
        foreach (var (key, node) in byName)
            if (dict.TryGetValue(key, out var v))
                result[key] = PruneValue(v, node);
        return result;
    }

    private static List<object?> PruneList(System.Collections.IEnumerable list, ListValueNode listNode)
    {
        var items = list.Cast<object?>().ToList();
        var result = new List<object?>(items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            var node = i < listNode.Items.Count ? listNode.Items[i] : null;
            result.Add(PruneValue(items[i], node));
        }
        return result;
    }

    internal static ObjectFieldConfiguration DeleteField(string collection)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.DeleteFieldName(collection), null,
            TypeReference.Parse("Boolean"),
            resolver: ctx => ResolveDelete(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration("id", null, TypeReference.Parse("ID!")));
        config.Arguments.Add(new ArgumentConfiguration("purge", null, TypeReference.Parse("Boolean")));
        return config;
    }

    private static async ValueTask<object?> ResolveDelete(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        var purge = ctx.ArgumentValue<bool?>("purge") ?? false;
        return await ctx.Service<IGraphQlDataSource>().DeleteAsync(collection, id, purge, ctx.RequestAborted);
    }

    internal static ObjectFieldConfiguration RestoreField(string collection)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.RestoreFieldName(collection), null,
            TypeReference.Parse(SchemaTypeMapper.TypeName(collection)),
            resolver: ctx => ResolveRestore(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration("id", null, TypeReference.Parse("ID!")));
        return config;
    }

    private static async ValueTask<object?> ResolveRestore(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        var restored = await ctx.Service<IGraphQlDataSource>().RestoreAsync(collection, id, ctx.RequestAborted);
        return restored;   // null -> GraphQL null (REST 404 parity); shape already matches a query node
    }

    internal static ObjectFieldConfiguration RevertField(string collection)
    {
        var config = new ObjectFieldConfiguration(
            SchemaTypeMapper.RevertFieldName(collection), null,
            TypeReference.Parse(SchemaTypeMapper.TypeName(collection)),
            resolver: ctx => ResolveRevert(ctx, collection));
        config.Arguments.Add(new ArgumentConfiguration("id", null, TypeReference.Parse("ID!")));
        config.Arguments.Add(new ArgumentConfiguration("revisionNumber", null, TypeReference.Parse("Int!")));
        return config;
    }

    private static async ValueTask<object?> ResolveRevert(IResolverContext ctx, string collection)
    {
        var id = ctx.ArgumentValue<string>("id");
        var n = (long)ctx.ArgumentValue<int>("revisionNumber");
        var reverted = await ctx.Service<IGraphQlDataSource>().RevertAsync(collection, id, n, ctx.RequestAborted);
        return reverted;   // null -> GraphQL null (REST 404 parity); shape already matches a query node
    }
}
