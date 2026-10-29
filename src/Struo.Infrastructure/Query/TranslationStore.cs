// src/Struo.Infrastructure/Query/TranslationStore.cs
using System.Reflection;
using SqlSugar;

namespace Struo.Infrastructure.Query;

internal sealed class TranslationStore(ISqlSugarClient db, TransactionRunner transactions)
{
    private static readonly GenericDispatcher<Func<TranslationStore, string, IReadOnlyList<object>, string, string?, CancellationToken, Task<IReadOnlyList<object>>>> LoadDispatcher =
        new(typeof(TranslationStore), nameof(LoadTranslationsGenericAsync),
            [typeof(string), typeof(IReadOnlyList<object>), typeof(string), typeof(string), typeof(CancellationToken)]);

    private static readonly GenericDispatcher<Func<TranslationStore, string, string, string, IReadOnlyList<string>, object, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>, CancellationToken, Task>> SyncDispatcher =
        new(typeof(TranslationStore), nameof(SyncTranslationsGenericAsync),
            [typeof(string), typeof(string), typeof(string), typeof(IReadOnlyList<string>), typeof(object),
             typeof(IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>), typeof(CancellationToken)]);

    private static readonly GenericDispatcher<Func<TranslationStore, string, string, IReadOnlyList<string>, object, IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>, CancellationToken, Task>> InsertDispatcher =
        new(typeof(TranslationStore), nameof(InsertTranslationsGenericAsync),
            [typeof(string), typeof(string), typeof(IReadOnlyList<string>), typeof(object),
             typeof(IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>>), typeof(CancellationToken)]);

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

    public async Task InsertTranslationsAsync(
        Type translationType,
        string fkProperty,
        string localeProperty,
        IReadOnlyList<string> fieldProperties,
        object parentId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> perLocale,
        CancellationToken ct = default) =>
        await InsertDispatcher.For(translationType)(this, fkProperty, localeProperty, fieldProperties, parentId, perLocale, ct);

    public async Task SyncTranslationsAsync(
        Type translationType,
        string fkProperty,
        string localeProperty,
        IReadOnlyList<string> fieldProperties,
        object parentId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> perLocale,
        CancellationToken ct = default)
    {
        var fkColumn = db.EntityMaintenance.GetDbColumnName(fkProperty, translationType);
        await SyncDispatcher.For(translationType)(this, fkColumn, fkProperty, localeProperty, fieldProperties, parentId, perLocale, ct);
    }

    // A create inserts under a parent id that is new in this transaction, so there is nothing to look up.
    private async Task InsertTranslationsGenericAsync<T>(
        string fkProperty,
        string localeProperty,
        IReadOnlyList<string> fieldProperties,
        object parentId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> perLocale,
        CancellationToken ct) where T : class, new()
    {
        if (perLocale.Count == 0) return;

        var rows = BuildRows<T>(fkProperty, localeProperty, fieldProperties, parentId, perLocale);
        await transactions.InTransactionAsync(async () => await db.Insertable(rows).ExecuteCommandAsync(ct), ct);
    }

    private async Task SyncTranslationsGenericAsync<T>(
        string fkColumn,
        string fkProperty,
        string localeProperty,
        IReadOnlyList<string> fieldProperties,
        object parentId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> perLocale,
        CancellationToken ct) where T : class, new()
    {
        if (perLocale.Count == 0) return;

        var type = typeof(T);
        var localeProp = type.GetProperty(localeProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var fieldProps = ResolveFieldProperties(type, fieldProperties);

        await transactions.InTransactionAsync(async () =>
        {
            // An update merges the sent fields onto the stored row of each supplied locale in place (by
            // primary key): a field the request omits keeps its stored value, an explicit null clears it.
            // A locale with no stored row is inserted. It issues no DELETE: a delete that matches
            // nothing takes gap locks on the (fk, locale) unique index, and those deadlock concurrent
            // updates of different items on InnoDB. The existing rows come from a plain consistent read.
            // Two concurrent updates of the same item are kept apart by the parent's version check
            // (the AuditableEntity optimistic lock), which runs before this sync in the same transaction.
            var existing = await LoadExistingAsync<T>(fkColumn, parentId, ct);
            var updates = new List<T>();
            var insertLocales = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (locale, values) in perLocale)
            {
                var stored = existing.Find(e => string.Equals(
                    (string?)localeProp.GetValue(e), locale, StringComparison.OrdinalIgnoreCase));
                if (stored is null) { insertLocales[locale] = values; continue; }
                ApplyValues(stored, fieldProps, values);
                updates.Add(stored);
            }

            if (updates.Count > 0) await db.Updateable(updates).ExecuteCommandAsync(ct);
            if (insertLocales.Count > 0)
                await db.Insertable(BuildRows<T>(fkProperty, localeProperty, fieldProperties, parentId, insertLocales))
                    .ExecuteCommandAsync(ct);
        }, ct);
    }

    // One row per supplied locale, built by reflection.
    private static List<T> BuildRows<T>(
        string fkProperty,
        string localeProperty,
        IReadOnlyList<string> fieldProperties,
        object parentId,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> perLocale) where T : class, new()
    {
        var type = typeof(T);
        var fkProp = type.GetProperty(fkProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var localeProp = type.GetProperty(localeProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var fieldProps = ResolveFieldProperties(type, fieldProperties);

        var rows = new List<T>(perLocale.Count);
        foreach (var (locale, values) in perLocale)
        {
            var row = new T();
            fkProp.SetValue(row, IdCoercion.Coerce(parentId, fkProp.PropertyType));
            localeProp.SetValue(row, locale);
            ApplyValues(row, fieldProps, values);
            rows.Add(row);
        }
        return rows;
    }

    private static Dictionary<string, PropertyInfo> ResolveFieldProperties(Type type, IReadOnlyList<string> fieldProperties) =>
        fieldProperties.ToDictionary(
            f => f,
            f => type.GetProperty(f, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                 ?? throw new InvalidOperationException(
                     $"Translation entity '{type.Name}' has no field property '{f}'."),
            StringComparer.OrdinalIgnoreCase);

    // Sets only the fields present in `values` (an explicit null is present); the rest keep the row's value.
    private static void ApplyValues<T>(
        T row, Dictionary<string, PropertyInfo> fieldProps, IReadOnlyDictionary<string, object?> values)
        where T : class
    {
        foreach (var (fieldName, prop) in fieldProps)
        {
            if (TryGetValueCaseInsensitive(values, fieldName, out var raw))
                prop.SetValue(row, CoerceValue(raw, prop.PropertyType));
        }
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
