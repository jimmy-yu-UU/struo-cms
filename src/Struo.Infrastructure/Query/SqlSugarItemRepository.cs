// src/Struo.Infrastructure/Query/SqlSugarItemRepository.cs
using System.Reflection;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Application.Query;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

public sealed class SqlSugarItemRepository(ISqlSugarClient db, IEntityRegistry registry) : IItemRepository
{
    // Cached generic method definitions — resolved once at class load, pinned by parameter-type signature.
    private static readonly MethodInfo RunQueryDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(RunQuery),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(List<IConditionalModel>), typeof(string), typeof(int), typeof(int)])!;

    private static readonly MethodInfo GetByIdGenericDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(GetByIdGeneric),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(object)])!;

    private static readonly MethodInfo CreateGenericDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(CreateGeneric),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(object)])!;

    private static readonly MethodInfo UpdateGenericDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(UpdateGeneric),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(object)])!;

    private static readonly MethodInfo DeleteGenericDef =
        typeof(SqlSugarItemRepository).GetMethod(nameof(DeleteGeneric),
            BindingFlags.NonPublic | BindingFlags.Instance,
            [typeof(object)])!;

    public Task<QueryResult> QueryAsync(string collection, QueryModel query,
        IReadOnlyList<string> searchableFields, CancellationToken ct = default)
    {
        var d = Descriptor(collection);
        var conditionals = ConditionalModelTranslator.Translate(query.Filter, query.Search, searchableFields, d, db);
        var orderBy = BuildOrderBy(query.Sort, d);
        var result = (QueryResult)RunQueryDef.MakeGenericMethod(d.EntityType)
            .Invoke(this, [conditionals, orderBy, query.Limit, query.Offset])!;
        return Task.FromResult(result);
    }

    private QueryResult RunQuery<T>(List<IConditionalModel> conditionals, string? orderBy, int limit, int offset)
        where T : class, new()
    {
        var pageNumber = (offset / Math.Max(1, limit)) + 1;
        var total = 0;
        var queryable = db.Queryable<T>().Where(conditionals);
        if (!string.IsNullOrWhiteSpace(orderBy)) queryable = queryable.OrderBy(orderBy);
        var rows = queryable.ToPageList(pageNumber, limit, ref total);
        return new QueryResult(rows.Cast<object>().ToList(), total);
    }

    public Task<object?> GetByIdAsync(string collection, string id, CancellationToken ct = default)
    {
        var d = Descriptor(collection);
        return Task.FromResult(
            (object?)GetByIdGenericDef.MakeGenericMethod(d.EntityType).Invoke(this, [ConvertId(id, d)]));
    }

    private object? GetByIdGeneric<T>(object id) where T : class, new() =>
        db.Queryable<T>().InSingle(id);

    public Task<object> CreateAsync(string collection, object entity, CancellationToken ct = default)
    {
        var d = Descriptor(collection);
        var created = CreateGenericDef.MakeGenericMethod(d.EntityType).Invoke(this, [entity])!;
        return Task.FromResult(created);
    }

    private object CreateGeneric<T>(object entity) where T : class, new() =>
        db.Insertable((T)entity).ExecuteReturnEntity()!;

    public async Task<object?> UpdateAsync(string collection, string id, object entity,
        CancellationToken ct = default)
    {
        var d = Descriptor(collection);
        var existing = await GetByIdAsync(collection, id, ct);
        if (existing is null) return null;

        // Clone so the caller's object is never mutated.
        var clone = CloneEntity(entity, d.EntityType);
        d.EntityType.GetProperty(d.IdProperty)!.SetValue(clone, ConvertId(id, d));
        UpdateGenericDef.MakeGenericMethod(d.EntityType).Invoke(this, [clone]);
        return await GetByIdAsync(collection, id, ct);
    }

    private void UpdateGeneric<T>(object entity) where T : class, new() =>
        db.Updateable((T)entity).ExecuteCommand();

    public async Task<bool> DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        var d = Descriptor(collection);
        var existing = await GetByIdAsync(collection, id, ct);
        if (existing is null) return false;
        DeleteGenericDef.MakeGenericMethod(d.EntityType).Invoke(this, [ConvertId(id, d)]);
        return true;
    }

    private void DeleteGeneric<T>(object id) where T : class, new() =>
        db.Deleteable<T>().In(id).ExecuteCommand();

    private EntityDescriptor Descriptor(string collection) =>
        registry.Get(collection) ?? throw new InvalidOperationException($"Unknown collection '{collection}'.");

    /// <summary>
    /// Converts a string ID to the PK property type. Handles Guid and all IConvertible types.
    /// </summary>
    private static object ConvertId(string id, EntityDescriptor d)
    {
        var idType = d.EntityType.GetProperty(d.IdProperty)!.PropertyType;
        var targetType = Nullable.GetUnderlyingType(idType) ?? idType;

        if (targetType == typeof(Guid))
            return Guid.Parse(id);

        try
        {
            return Convert.ChangeType(id, targetType);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            throw new ArgumentException(
                $"ID value '{id}' cannot be converted to type '{targetType.Name}' for collection '{d.EntityType.Name}'.",
                nameof(id), ex);
        }
    }

    /// <summary>Shallow-clones an entity to avoid mutating the caller's object.</summary>
    private static object CloneEntity(object entity, Type entityType)
    {
        var clone = Activator.CreateInstance(entityType)!;
        foreach (var prop in entityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                     .Where(p => p.CanRead && p.CanWrite))
        {
            prop.SetValue(clone, prop.GetValue(entity));
        }
        return clone;
    }

    private string? BuildOrderBy(IReadOnlyList<SortField> sort, EntityDescriptor d)
    {
        if (sort.Count == 0) return null;
        var parts = sort.Select(s =>
        {
            var prop = d.FieldToProperty.TryGetValue(s.Field, out var p) ? p : s.Field;
            var col = db.EntityMaintenance.GetDbColumnName(prop, d.EntityType);
            return $"{col} {(s.Descending ? "DESC" : "ASC")}";
        });
        return string.Join(", ", parts);
    }
}
