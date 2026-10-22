// src/Struo.Infrastructure/Query/TranslationStore.cs
using System.Reflection;
using SqlSugar;

namespace Struo.Infrastructure.Query;

internal sealed class TranslationStore(ISqlSugarClient db, TransactionRunner transactions)
{
    private static readonly GenericDispatcher<Func<TranslationStore, string, IReadOnlyList<object>, string, string?, CancellationToken, Task<IReadOnlyList<object>>>> LoadDispatcher =
        new(typeof(TranslationStore), nameof(LoadTranslationsGenericAsync),
            [typeof(string), typeof(IReadOnlyList<object>), typeof(string), typeof(string), typeof(CancellationToken)]);

    private static readonly GenericDispatcher<Func<TranslationStore, string, string, string, IReadOnlyList<string>, object, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>, bool, CancellationToken, Task>> SyncDispatcher =
        new(typeof(TranslationStore), nameof(SyncTranslationsGenericAsync),
            [typeof(string), typeof(string), typeof(string), typeof(IReadOnlyList<string>), typeof(object),
             typeof(IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>), typeof(bool), typeof(CancellationToken)]);

    public async Task<IReadOnlyList<object>> LoadTranslationsAsync(
        Type translationType,
        string fkProperty,
        string localeProperty,
        IReadOnlyList<object> parentIds,
        string? locale,
        CancellationToken ct = default)
    {
        if (parentIds.Count == 0) return [];

        var fkColumn = db.EntityMaintenance.GetDbColumnName(fkProperty, translationType);
        var localeColumn = db.EntityMaintenance.GetDbColumnName(localeProperty, translationType);
        return await LoadDispatcher.For(translationType)(this, fkColumn, parentIds, localeColumn, locale, ct);
    }

    private async Task<IReadOnlyList<object>> LoadTranslationsGenericAsync<T>(
        string fkColumn, IReadOnlyList<object> parentIds, string localeColumn, string? locale,
        CancellationToken ct) where T : class, new()
    {
        // ConditionalType.In on the FK keeps "bigint IN (...)" Postgres-safe (no text coercion).
        var conditionals = new List<IConditionalModel>
        {
            new ConditionalModel
            {
                FieldName = fkColumn,
                ConditionalType = ConditionalType.In,
                FieldValue = string.Join(",", parentIds.Select(v => v?.ToString())),
                CSharpTypeName = RepositoryHelpers.TypeNameOf(parentIds.FirstOrDefault(v => v is not null))  // parent FK is Guid
            }
        };
        if (locale is not null)
        {
            conditionals.Add(new ConditionalModel
            {
                FieldName = localeColumn,
                ConditionalType = ConditionalType.Equal,
                FieldValue = locale
            });
        }
        var rows = await db.Queryable<T>().Where(conditionals).ToListAsync(ct);
        return rows.Cast<object>().ToList();
    }

    public async Task SyncTranslationsAsync(
        Type translationType,
        string fkProperty,
        string localeProperty,
        IReadOnlyList<string> fieldProperties,
        object parentId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> perLocale,
        bool isCreate,
        CancellationToken ct = default)
    {
        var fkColumn = db.EntityMaintenance.GetDbColumnName(fkProperty, translationType);
        await SyncDispatcher.For(translationType)(this, fkColumn, fkProperty, localeProperty, fieldProperties, parentId, perLocale, isCreate, ct);
    }

    private async Task SyncTranslationsGenericAsync<T>(
        string fkColumn,
        string fkProperty,
        string localeProperty,
        IReadOnlyList<string> fieldProperties,
        object parentId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> perLocale,
        bool isCreate,
        CancellationToken ct) where T : class, new()
    {
        if (perLocale.Count == 0) return;

        var type = typeof(T);
        var fkProp = type.GetProperty(fkProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var localeProp = type.GetProperty(localeProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var fieldProps = fieldProperties.ToDictionary(
            f => f,
            f => type.GetProperty(f, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                 ?? throw new InvalidOperationException(
                     $"Translation entity '{type.Name}' has no field property '{f}'."),
            StringComparer.OrdinalIgnoreCase);

        // Build one row per supplied locale outside the transaction (reflection only).
        var rows = new List<T>(perLocale.Count);
        foreach (var (locale, values) in perLocale)
        {
            var row = new T();
            fkProp.SetValue(row, IdCoercion.Coerce(parentId, fkProp.PropertyType));
            localeProp.SetValue(row, locale);
            foreach (var (fieldName, prop) in fieldProps)
            {
                if (TryGetValueCaseInsensitive(values, fieldName, out var raw))
                    prop.SetValue(row, CoerceValue(raw, prop.PropertyType));
            }
            rows.Add(row);
        }

        await transactions.InTransactionAsync(async () =>
        {
            // A create inserts under a parent id that is new in this transaction, so there is nothing
            // to look up.
            if (isCreate)
            {
                await db.Insertable(rows).ExecuteCommandAsync(ct);
                return;
            }

            // An update replaces the stored row of each supplied locale in place (by primary key) and
            // inserts a row for a locale that has none. It issues no DELETE: a delete that matches
            // nothing takes gap locks on the (fk, locale) unique index, and those deadlock concurrent
            // updates of different items on InnoDB. The existing rows come from a plain consistent read.
            // Two concurrent updates of the same item are kept apart by the parent's version check
            // (the AuditableEntity optimistic lock), which runs before this sync in the same transaction.
            var existing = await LoadExistingAsync<T>(fkColumn, parentId, ct);
            var pk = PrimaryKeyOf(type);
            var updates = new List<T>();
            var inserts = new List<T>();
            foreach (var row in rows)
            {
                var stored = existing.Find(e => string.Equals(
                    (string?)localeProp.GetValue(e), (string?)localeProp.GetValue(row), StringComparison.OrdinalIgnoreCase));
                if (stored is null) { inserts.Add(row); continue; }
                pk.SetValue(row, pk.GetValue(stored));
                updates.Add(row);
            }

            if (updates.Count > 0) await db.Updateable(updates).ExecuteCommandAsync(ct);
            if (inserts.Count > 0) await db.Insertable(inserts).ExecuteCommandAsync(ct);
        }, ct);
    }

    private async Task<List<T>> LoadExistingAsync<T>(string fkColumn, object parentId, CancellationToken ct)
        where T : class, new() =>
        await db.Queryable<T>().Where(
        [
            new ConditionalModel
            {
                FieldName = fkColumn,
                ConditionalType = ConditionalType.In,
                FieldValue = parentId.ToString(),
                CSharpTypeName = RepositoryHelpers.TypeNameOf(parentId)  // parent FK is Guid
            }
        ]).ToListAsync(ct);

    private PropertyInfo PrimaryKeyOf(Type type)
    {
        var column = db.EntityMaintenance.GetEntityInfo(type).Columns.Single(c => c.IsPrimarykey);
        return type.GetProperty(column.PropertyName, BindingFlags.Public | BindingFlags.Instance)!;
    }

    private static bool TryGetValueCaseInsensitive(
        IReadOnlyDictionary<string, object?> dict, string key, out object? value)
    {
        if (dict.TryGetValue(key, out value)) return true;
        foreach (var (k, v) in dict)
        {
            if (string.Equals(k, key, StringComparison.OrdinalIgnoreCase)) { value = v; return true; }
        }
        value = null;
        return false;
    }

    private static object? CoerceValue(object? raw, Type targetType) => IdCoercion.Coerce(raw, targetType);
}
