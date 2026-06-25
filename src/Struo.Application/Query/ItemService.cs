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
        var rows = result.Rows.Select(r => Project(r, meta, validated.Fields)).ToList();
        return new PagedResult(rows, result.Total, validated.Limit, validated.Offset);
    }

    public async Task<IReadOnlyDictionary<string, object?>?> GetAsync(string collection, string id, CancellationToken ct = default)
    {
        var meta = Meta(collection);
        var entity = await repository.GetByIdAsync(collection, id, ct);
        return entity is null ? null : Project(entity, meta, null);
    }

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
