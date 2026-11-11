// src/Struo.Infrastructure/Query/TranslationStore.cs
using System.Reflection;
using SqlSugar;
using Struo.Application.Query;

namespace Struo.Infrastructure.Query;

internal sealed class TranslationStore(ISqlSugarClient db, TransactionRunner transactions)
{
    private static readonly GenericDispatcher<Func<TranslationStore, ValueTarget, IReadOnlyList<object>, string, string?, CancellationToken, Task<IReadOnlyList<object>>>> LoadDispatcher =
        new(typeof(TranslationStore), nameof(LoadTranslationsGenericAsync),
            [typeof(ValueTarget), typeof(IReadOnlyList<object>), typeof(string), typeof(string), typeof(CancellationToken)]);

    private static readonly GenericDispatcher<Func<TranslationStore, TranslationSyncRequest, ValueTarget, CancellationToken, Task>> SyncDispatcher =
        new(typeof(TranslationStore), nameof(SyncTranslationsGenericAsync),
            [typeof(TranslationSyncRequest), typeof(ValueTarget), typeof(CancellationToken)]);

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

        var fkTarget = WhereInQueries.TargetOf(db, translationType, fkProperty);
        var localeColumn = db.EntityMaintenance.GetDbColumnName(localeProperty, translationType);
        return await LoadDispatcher.For(translationType)(this, fkTarget, parentIds, localeColumn, locale, ct);
    }

    private async Task<IReadOnlyList<object>> LoadTranslationsGenericAsync<T>(
        ValueTarget fkTarget, IReadOnlyList<object> parentIds, string localeColumn, string? locale,
        CancellationToken ct) where T : class, new()
    {
        // The typed set primitive on the FK keeps "bigint IN (...)" Postgres-safe (no text coercion).
        var conditionals = new List<IConditionalModel> { WhereInQueries.InCondition(fkTarget, parentIds) };
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

    public async Task SyncTranslationsAsync(TranslationSyncRequest request, CancellationToken ct = default)
    {
        var fkTarget = WhereInQueries.TargetOf(db, request.TranslationType, request.FkProperty);
        await SyncDispatcher.For(request.TranslationType)(this, request, fkTarget, ct);
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
        TranslationSyncRequest request, ValueTarget fkTarget, CancellationToken ct) where T : class, new()
    {
        var perLocale = request.PerLocale;
        var deleteLocales = request.DeleteLocales;
        if (perLocale.Count == 0 && deleteLocales.Count == 0) return;

        var type = typeof(T);
        var localeProp = type.GetProperty(request.LocaleProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var fieldProps = ResolveFieldProperties(type, request.FieldProperties);

        await transactions.InTransactionAsync(async () =>
        {
            // An update merges the sent fields onto the stored row of each supplied locale in place (by
            // primary key): a field the request omits keeps its stored value, an explicit null clears it.
            // A locale with no stored row is inserted. A locale to delete is removed only when the read
            // below found its row, and by primary key. No DELETE is keyed on (fk, locale): one that
            // matches nothing takes gap locks on that unique index, and those deadlock concurrent
            // updates of different items on InnoDB. The existing rows come from a plain consistent read.
            // Two concurrent updates of the same item are kept apart by the parent's version check
            // (the AuditableEntity optimistic lock), which runs before this sync in the same transaction.
            var existing = await LoadExistingAsync<T>(fkTarget, request.ParentId, ct);
            var updates = new List<T>();
            var insertLocales = new Dictionary<string, IReadOnlyDictionary<string, object?>>(StringComparer.OrdinalIgnoreCase);
            foreach (var (locale, values) in perLocale)
            {
                var stored = FindStored(existing, localeProp, locale);
                if (stored is null) { insertLocales[locale] = values; continue; }
                ApplyValues(stored, fieldProps, values);
                updates.Add(stored);
            }
            var deletes = deleteLocales
                .Select(locale => FindStored(existing, localeProp, locale))
                .OfType<T>()
                .ToList();

            if (updates.Count > 0) await db.Updateable(updates).ExecuteCommandAsync(ct);
            if (insertLocales.Count > 0)
                await db.Insertable(BuildRows<T>(
                    request.FkProperty, request.LocaleProperty, request.FieldProperties, request.ParentId, insertLocales))
                    .ExecuteCommandAsync(ct);
            if (deletes.Count > 0) await db.Deleteable(deletes).ExecuteCommandAsync(ct);
        }, ct);
    }

    private static T? FindStored<T>(List<T> existing, PropertyInfo localeProp, string locale) where T : class =>
        existing.Find(e => string.Equals((string?)localeProp.GetValue(e), locale, StringComparison.OrdinalIgnoreCase));

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

    private async Task<List<T>> LoadExistingAsync<T>(ValueTarget fkTarget, object parentId, CancellationToken ct)
        where T : class, new() =>
        await db.Queryable<T>().Where([WhereInQueries.InCondition(fkTarget, [parentId])]).ToListAsync(ct);

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
