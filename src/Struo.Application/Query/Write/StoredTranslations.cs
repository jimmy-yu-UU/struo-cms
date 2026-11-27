using Struo.Domain.Metadata.Models;

namespace Struo.Application.Query;

/// <summary>
/// The translation rows of the item one update writes, loaded on first use and then shared, so the checks that
/// compare file links with the stored ones read them once between them.
/// </summary>
internal sealed class StoredTranslations(IItemRepository repository, TranslationMetadata tm, object parentId)
{
    private Task<IReadOnlyList<object>>? rows;

    internal TranslationMetadata Metadata => tm;

    internal Task<IReadOnlyList<object>> RowsAsync(CancellationToken ct) =>
        rows ??= repository.LoadTranslationsAsync(
            tm.TranslationEntityType, tm.ForeignKeyProperty, tm.LocaleProperty, [parentId], null, ct);
}
