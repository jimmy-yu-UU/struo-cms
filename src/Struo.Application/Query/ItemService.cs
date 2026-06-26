// src/Struo.Application/Query/ItemService.cs
using System.Text.Json;
using Struo.Application.Configuration;
using Struo.Application.Metadata;
using Struo.Application.Security;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

public sealed record PagedResult(
    IReadOnlyList<IReadOnlyDictionary<string, object?>> Data, int Total, int Limit, int Offset);

public sealed class ItemService(
    IItemRepository repository,
    IMetadataProvider metadata,
    IEntityRegistry registry,
    IPermissionService permissions,
    IRelationshipGraph graph,
    IRelationExpander expander,
    StruoQueryOptions options)
{
    private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

    public async Task<PagedResult> QueryAsync(string collection, QueryModel raw, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanRead(collection)) throw new QueryException("Read not permitted.");
        var validated = QueryValidator.Validate(raw, meta, options);
        var searchable = QueryValidator.SearchableFields(meta);
        var result = await repository.QueryAsync(collection, validated, searchable, ct);

        var entities = result.Rows;
        var rows = entities.Select(r => Project(r, meta, validated.Fields)).ToList();
        await ExpandDeepAsync(collection, raw.Deep, entities, rows, ct);
        return new PagedResult(rows, result.Total, validated.Limit, validated.Offset);
    }

    public async Task<IReadOnlyDictionary<string, object?>?> GetAsync(
        string collection, string id, DeepSpec? deep = null, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanRead(collection)) throw new QueryException("Read not permitted.");
        var entity = await repository.GetByIdAsync(collection, id, ct);
        if (entity is null) return null;

        var projected = (Dictionary<string, object?>)Project(entity, meta, null);
        await ExpandDeepAsync(collection, deep, [entity], [projected], ct);
        return projected;
    }

    /// <summary>
    /// Validates the requested deep relations against the relationship graph and the
    /// configured <see cref="StruoQueryOptions.MaxRelationDepth"/>, then nests the expanded
    /// (batched) relation rows into each projected parent dictionary. No-op when
    /// <paramref name="deep"/> is null/empty or there are no parent rows.
    /// </summary>
    private async Task ExpandDeepAsync(
        string collection, DeepSpec? deep,
        IReadOnlyList<object> entities, IReadOnlyList<IReadOnlyDictionary<string, object?>> rows,
        CancellationToken ct)
    {
        if (deep is null || deep.Relations.Count == 0 || entities.Count == 0) return;

        // Validate: deep expansion is single-level here, so MaxRelationDepth caps the number
        // of relations expanded in one request (each adds one level of nesting / one batch query).
        if (deep.Relations.Count > options.MaxRelationDepth)
            throw new QueryException(
                $"Too many deep relations requested ({deep.Relations.Count}); the maximum is {options.MaxRelationDepth}.");
        foreach (var relName in deep.Relations.Keys)
        {
            if (graph.Resolve(collection, relName) is null)
                throw new QueryException($"Unknown relation '{relName}' on '{collection}'.");
        }

        var parentDesc = registry.Get(collection)!;

        object ParentId(object entity) =>
            ReadProp(entity, parentDesc.IdProperty)
            ?? throw new QueryException($"Cannot expand relations: a '{collection}' row has no id.");

        var nested = await expander.ExpandAsync(
            collection, entities, deep, ProjectFor, ParentId, ReadProp, ct);

        for (var i = 0; i < entities.Count; i++)
        {
            var pid = ParentId(entities[i]);
            if (!nested.TryGetValue(pid, out var relMap)) continue;
            var dict = (Dictionary<string, object?>)rows[i];
            foreach (var (relName, value) in relMap) dict[relName] = value;
        }
    }

    /// <summary>Reuses the metadata projection for an arbitrary target collection.</summary>
    private IReadOnlyDictionary<string, object?> ProjectFor(
        string collectionName, object entity, IReadOnlyList<string>? fields) =>
        Project(entity, Meta(collectionName), fields);

    /// <summary>
    /// Reads a property value off an entity by name. The name may be a CLR property name
    /// (e.g. junction <c>ArticleId</c>) or a camelCase field name (e.g. <c>authorId</c>, <c>id</c>),
    /// so the lookup is case-insensitive.
    /// </summary>
    private static object? ReadProp(object entity, string propertyName) =>
        entity.GetType()
            .GetProperty(propertyName,
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.IgnoreCase)
            ?.GetValue(entity);

    public async Task<IReadOnlyDictionary<string, object?>> CreateAsync(string collection, JsonElement body, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanWrite(collection)) throw new QueryException("Write not permitted.");
        var entity = Deserialize(collection, body, meta);
        var created = await repository.CreateAsync(collection, entity, ct);
        return Project(created, meta, null);
    }

    public async Task<IReadOnlyDictionary<string, object?>?> UpdateAsync(string collection, string id, JsonElement body, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        if (!permissions.CanWrite(collection)) throw new QueryException("Write not permitted.");
        var entity = Deserialize(collection, body, meta);
        var updated = await repository.UpdateAsync(collection, id, entity, ct);
        return updated is null ? null : Project(updated, meta, null);
    }

    public Task<bool> DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        _ = Meta(collection);
        if (!permissions.CanDelete(collection)) throw new QueryException("Delete not permitted.");
        return repository.DeleteAsync(collection, id, ct);
    }

    private CollectionMetadata Meta(string collection) =>
        metadata.GetCollection(collection) ?? throw new CollectionNotFoundException(collection);

    private object Deserialize(string collection, JsonElement body, CollectionMetadata meta)
    {
        var d = registry.Get(collection) ?? throw new CollectionNotFoundException(collection);
        var entity = body.Deserialize(d.EntityType, JsonOpts)
                     ?? throw new QueryException("Request body could not be parsed.");

        // strip system/read-only fields (audit AOP / identity own them); only nullable props can be nulled
        foreach (var field in meta.Fields.Where(f => f.IsSystem || f.ReadOnly))
        {
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            var pi = d.EntityType.GetProperty(prop);
            if (pi is not { CanWrite: true }) continue;
            var canBeNull = !pi.PropertyType.IsValueType || Nullable.GetUnderlyingType(pi.PropertyType) is not null;
            if (canBeNull) pi.SetValue(entity, null);
        }

        // required validation
        foreach (var field in meta.Fields.Where(f => f.Required))
        {
            var pi = d.FieldToProperty.TryGetValue(field.Name, out var prop) ? d.EntityType.GetProperty(prop) : null;
            var value = pi?.GetValue(entity);
            if (value is null || (value is string s && string.IsNullOrWhiteSpace(s)))
                throw new QueryException($"Field '{field.Name}' is required.");
        }
        return entity;
    }

    private IReadOnlyDictionary<string, object?> Project(object entity, CollectionMetadata meta, IReadOnlyList<string>? fields)
    {
        var d = registry.Get(meta.Name)!;
        var dict = new Dictionary<string, object?>();

        const string idKey = "id";
        dict[idKey] = d.EntityType.GetProperty(d.IdProperty)?.GetValue(entity);

        var wanted = fields is { Count: > 0 } ? fields.ToHashSet(StringComparer.OrdinalIgnoreCase) : null;
        var readable = permissions.ReadableFields(meta.Name, meta.Fields.Select(f => f.Name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var field in meta.Fields)
        {
            if (field.Hidden) continue;
            if (field.Name == idKey) continue;
            if (wanted is not null && !wanted.Contains(field.Name)) continue;
            if (!readable.Contains(field.Name)) continue;
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            dict[field.Name] = d.EntityType.GetProperty(prop)?.GetValue(entity);
        }
        return dict;
    }
}
