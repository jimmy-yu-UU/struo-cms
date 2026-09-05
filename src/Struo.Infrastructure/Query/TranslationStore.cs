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
        CancellationToken ct = default)
    {
        var fkColumn = db.EntityMaintenance.GetDbColumnName(fkProperty, translationType);
        await SyncDispatcher.For(translationType)(this, fkColumn, fkProperty, localeProperty, fieldProperties, parentId, perLocale, ct);
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
        var fkProp = type.GetProperty(fkProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var localeProp = type.GetProperty(localeProperty, BindingFlags.Public | BindingFlags.Instance)!;
        var fieldProps = fieldProperties.ToDictionary(
            f => f,
            f => type.GetProperty(f, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase)
                 ?? throw new InvalidOperationException(
                     $"Translation entity '{type.Name}' has no field property '{f}'."),
            StringComparer.OrdinalIgnoreCase);

        // Build the rows + per-locale delete models outside the transaction (reflection only).
        var inserts = new List<T>(perLocale.Count);
        var deletes = new List<List<IConditionalModel>>(perLocale.Count);
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
            inserts.Add(row);

            // ConditionalType.In on the FK (Postgres-safe), Equal on the string locale.
            deletes.Add(
            [
                new ConditionalModel
                {
                    FieldName = fkColumn,
                    ConditionalType = ConditionalType.In,
                    FieldValue = parentId.ToString(),
                    CSharpTypeName = RepositoryHelpers.TypeNameOf(parentId)  // parent FK is Guid
                },
                new ConditionalModel
                {
                    FieldName = db.EntityMaintenance.GetDbColumnName(localeProperty, type),
                    ConditionalType = ConditionalType.Equal,
                    FieldValue = locale
                }
            ]);
        }

        await transactions.InTransactionAsync(async () =>
        {
            foreach (var del in deletes)
                await db.Deleteable<T>().Where(del).ExecuteCommandAsync(ct);
            await db.Insertable(inserts).ExecuteCommandAsync(ct);
        }, ct);
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
