// src/Struo.Infrastructure/Query/TranslationStore.cs
using System.Linq.Expressions;
using System.Reflection;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Query;

namespace Struo.Infrastructure.Query;

// CS9113 (unread primary constructor parameter): `transactions` is unused until
// SyncTranslationsAsync moves in here (Task 7, commit 3/3); suppressed transiently and
// restored once that commit lands.
#pragma warning disable CS9113
internal sealed class TranslationStore(ISqlSugarClient db, TransactionRunner transactions)
#pragma warning restore CS9113
{
    private static readonly GenericDispatcher<Func<TranslationStore, string, IReadOnlyList<object>, string, string?, CancellationToken, Task<IReadOnlyList<object>>>> LoadDispatcher =
        new(typeof(TranslationStore), nameof(LoadTranslationsGenericAsync),
            [typeof(string), typeof(IReadOnlyList<object>), typeof(string), typeof(string), typeof(CancellationToken)]);

    private static readonly GenericDispatcher<Func<TranslationStore, string, string, string, string, List<IConditionalModel>, CancellationToken, Task<IReadOnlyList<object>>>> ParentIdsDispatcher =
        new(typeof(TranslationStore), nameof(QueryTranslationParentIdsGenericAsync),
            [typeof(string), typeof(string), typeof(string), typeof(string), typeof(List<IConditionalModel>), typeof(CancellationToken)]);

    // Second-level dispatcher (T fixed by the call above, TFk resolved at runtime from the FK
    // property's actual CLR type) so the FK-only SQL projection below can be expressed as a genuinely
    // typed `Expression<Func<T, TFk>>` — SqlSugar's Select() does not translate a boxed
    // `Convert(member, object)` lambda into a single-column projection.
    private static readonly BiGenericDispatcher<Func<TranslationStore, List<IConditionalModel>, LambdaExpression, CancellationToken, Task<IReadOnlyList<object>>>> FkSelectDispatcher =
        new(typeof(TranslationStore), nameof(QueryTranslationFkSelectGenericAsync),
            [typeof(List<IConditionalModel>), typeof(LambdaExpression), typeof(CancellationToken)]);

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

    public async Task<IReadOnlyList<object>> QueryTranslationParentIdsAsync(
        Type translationType,
        string fkProperty,
        string localeProperty,
        string locale,
        FilterNode fieldCondition,
        CancellationToken ct = default)
    {
        var fkColumn = db.EntityMaintenance.GetDbColumnName(fkProperty, translationType);
        var localeColumn = db.EntityMaintenance.GetDbColumnName(localeProperty, translationType);

        // Build a fake EntityDescriptor for the translation type so ConditionalModelTranslator
        // can map camelCase field names to DB columns.
        var translationDescriptor = BuildTranslationDescriptor(translationType);

        // Locale equality filter (AND'd with the field condition below).
        var localeConditional = new ConditionalModel
        {
            FieldName = localeColumn,
            ConditionalType = ConditionalType.Equal,
            FieldValue = locale
        };

        // Field condition translated via the translation entity's column map.
        var fieldConditionals = ConditionalModelTranslator.Translate(fieldCondition, null, [], translationDescriptor, db);

        // Combine: locale AND field.  SqlSugar AND's consecutive IConditionalModel items.
        var conditionals = new List<IConditionalModel> { localeConditional };
        conditionals.AddRange(fieldConditionals);

        return await ParentIdsDispatcher.For(translationType)(this, fkColumn, fkProperty, localeColumn, locale, conditionals, ct);
    }

    private async Task<IReadOnlyList<object>> QueryTranslationParentIdsGenericAsync<T>(
        string fkColumn, string fkProperty, string localeColumn, string locale,
        List<IConditionalModel> conditionals, CancellationToken ct) where T : class, new()
    {
        var fkProp = typeof(T).GetProperty(fkProperty,
            BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (fkProp is null) return [];

        // Project ONLY the FK column at the SQL level instead of materializing whole
        // translation rows (which carry potentially-large body/text columns) just to read one value
        // out of each. The lambda's return type must be the FK's REAL CLR type (Guid/string/...) —
        // SqlSugar's Select() does not turn a boxed `Convert(member, object)` body into a one-column
        // projection, it silently produces an empty/garbage result — so TFk is resolved and dispatched
        // via a second generic layer below rather than boxed here.
        var param = Expression.Parameter(typeof(T), "x");
        var selectBody = Expression.Property(param, fkProp);
        var selector = Expression.Lambda(selectBody, param);

        return await FkSelectDispatcher.For(typeof(T), fkProp.PropertyType)(this, conditionals, selector, ct);
    }

    private async Task<IReadOnlyList<object>> QueryTranslationFkSelectGenericAsync<T, TFk>(
        List<IConditionalModel> conditionals, LambdaExpression selector, CancellationToken ct) where T : class, new()
    {
        var typedSelector = (Expression<Func<T, TFk>>)selector;
        var values = await db.Queryable<T>().Where(conditionals).Select(typedSelector).ToListAsync(ct);
        return values
            .Cast<object>()
            .Where(v => v is not null)
            .Distinct()
            .ToList();
    }

    /// <summary>
    /// Builds a minimal <see cref="EntityDescriptor"/> for a translation entity type so that
    /// <see cref="ConditionalModelTranslator"/> can resolve camelCase field names to DB columns.
    /// Only the <c>FieldToProperty</c> map and <c>IdProperty</c> are needed.
    /// </summary>
    private static EntityDescriptor BuildTranslationDescriptor(Type translationType)
    {
        // Build a camelCase -> CLR property name map for all public instance properties.
        var map = translationType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .ToDictionary(
                p => char.ToLowerInvariant(p.Name[0]) + p.Name[1..],
                p => p.Name,
                StringComparer.OrdinalIgnoreCase);

        // Id property: first property decorated with IsPrimaryKey, fall back to "Id".
        var idProp = translationType
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .FirstOrDefault(p => p.GetCustomAttribute<SugarColumn>() is { IsPrimaryKey: true })
            ?.Name ?? "Id";

        return new EntityDescriptor(translationType, map, idProp);
    }
}
