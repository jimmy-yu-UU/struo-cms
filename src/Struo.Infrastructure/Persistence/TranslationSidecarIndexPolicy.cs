using System.Reflection;
using SqlSugar;
using Struo.Domain.Metadata.Models;

namespace Struo.Infrastructure.Persistence;

/// <summary>
/// Maps each translation sidecar entity to the unique (foreign-key, locale) index it needs, derived
/// from <see cref="CollectionMetadata.Translation"/>. Group names deliberately match the legacy
/// hand-written <c>[SugarColumn(UniqueGroupNameList = [...])]</c> names (e.g.
/// <c>ux_file_translations_fk_locale</c>) so a fork still carrying the attribute dedupes to one index.
/// </summary>
public sealed record TranslationSidecarKey(string ForeignKeyProperty, string LocaleProperty, string GroupName);

public sealed class TranslationSidecarIndexPolicy
{
    public static TranslationSidecarIndexPolicy None { get; } =
        new(new Dictionary<Type, TranslationSidecarKey>());

    private readonly IReadOnlyDictionary<Type, TranslationSidecarKey> _sidecars;

    public TranslationSidecarIndexPolicy(IReadOnlyDictionary<Type, TranslationSidecarKey> sidecars)
    {
        ArgumentNullException.ThrowIfNull(sidecars);
        _sidecars = new Dictionary<Type, TranslationSidecarKey>(sidecars);
    }

    public static TranslationSidecarIndexPolicy FromMetadata(IEnumerable<CollectionMetadata> collections)
    {
        ArgumentNullException.ThrowIfNull(collections);
        var map = new Dictionary<Type, TranslationSidecarKey>();
        foreach (var t in collections.Select(c => c.Translation).Where(t => t is not null))
        {
            map[t!.TranslationEntityType] = KeyFor(t.TranslationEntityType, t.ForeignKeyProperty, t.LocaleProperty);
        }
        return new TranslationSidecarIndexPolicy(map);
    }

    public static TranslationSidecarKey KeyFor(Type sidecarType, string foreignKeyProperty, string localeProperty)
    {
        var table = sidecarType.GetCustomAttribute<SugarTable>()?.TableName;
        if (string.IsNullOrWhiteSpace(table)) table = sidecarType.Name.ToLowerInvariant();
        return new TranslationSidecarKey(foreignKeyProperty, localeProperty, $"ux_{table}_fk_locale");
    }

    public string? UniqueGroupFor(Type entityType, string propertyName)
    {
        if (!_sidecars.TryGetValue(entityType, out var key)) return null;
        var isKeyColumn = string.Equals(propertyName, key.ForeignKeyProperty, StringComparison.Ordinal)
                          || string.Equals(propertyName, key.LocaleProperty, StringComparison.Ordinal);
        return isKeyColumn ? key.GroupName : null;
    }
}
