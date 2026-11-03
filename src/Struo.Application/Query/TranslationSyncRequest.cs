// src/Struo.Application/Query/TranslationSyncRequest.cs
namespace Struo.Application.Query;

/// <summary>
/// The input of <see cref="IItemRepository.SyncTranslationsAsync"/>: the translation rows of one existing
/// parent to merge, insert or delete.
/// </summary>
/// <param name="TranslationType">The translation entity type.</param>
/// <param name="FkProperty">CLR property name of the foreign key to the parent.</param>
/// <param name="LocaleProperty">CLR property name of the locale column.</param>
/// <param name="FieldProperties">CLR property names of the translatable fields.</param>
/// <param name="ParentId">The parent row's primary key.</param>
/// <param name="PerLocale">Locale → camelCase field → value; only the fields sent are set.</param>
public sealed record TranslationSyncRequest(
    Type TranslationType,
    string FkProperty,
    string LocaleProperty,
    IReadOnlyList<string> FieldProperties,
    object ParentId,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, object?>> PerLocale)
{
    /// <summary>Locales whose stored row is deleted; empty when nothing is deleted.</summary>
    public IReadOnlyCollection<string> DeleteLocales { get; init; } = [];
}
