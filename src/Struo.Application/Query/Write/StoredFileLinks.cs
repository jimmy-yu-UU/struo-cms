// src/Struo.Application/Query/Write/StoredFileLinks.cs
using System.Text.Json;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Query;

/// <summary>
/// The file fields an item stores (root and per locale), kept in one comparable form, to tell whether a body
/// sends a field back unchanged. A <c>File</c>/<c>Image</c> value is its id or none; a <c>Files</c> value is its
/// ids in order. A value that is no id never equals a stored one.
/// </summary>
internal sealed class StoredFileLinks
{
    private const string None = "~";
    private const string NotAnId = "?";

    private readonly Dictionary<string, string> root = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Dictionary<string, string>> translated = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>True when <paramref name="sent"/> holds exactly the value the item stores for that field.</summary>
    internal bool Holds(SentFile sent)
    {
        var stored = sent.Locale is null
            ? root.GetValueOrDefault(sent.Field.Name)
            : translated.GetValueOrDefault(sent.Locale)?.GetValueOrDefault(sent.Field.Name) ?? NoneOrList(sent.Field);
        return stored is not null && stored == Canonical(sent.Field.Interface, sent.Value);
    }

    /// <summary>Reads the file fields of <paramref name="existing"/> and, when the collection has translations, of its sidecar rows.</summary>
    internal static async Task<StoredFileLinks> LoadAsync(
        CollectionMetadata meta, EntityDescriptor d, object existing, StoredTranslations? translations, CancellationToken ct)
    {
        var links = new StoredFileLinks();
        foreach (var field in FileFields.Root(meta))
        {
            if (!d.FieldToProperty.TryGetValue(field.Name, out var prop)) continue;
            links.root[field.Name] = Canonical(field.Interface, d.Properties.GetValueOrDefault(prop)?.GetValue(existing));
        }
        if (translations is not null && FileFields.Translated(meta).Any())
            links.AddTranslated(meta, translations.Metadata, await translations.RowsAsync(ct));
        return links;
    }

    private void AddTranslated(CollectionMetadata meta, TranslationMetadata tm, IReadOnlyList<object> rows)
    {
        var fields = FileFields.Translated(meta).ToList();
        foreach (var row in rows)
        {
            if (PropertyAccessorCache.Read(row, tm.LocaleProperty) is not string locale) continue;
            translated[locale] = fields.ToDictionary(
                f => f.Name, f => Canonical(f.Interface, PropertyAccessorCache.Read(row, f.Name)), StringComparer.OrdinalIgnoreCase);
        }
    }

    // A locale without a stored row holds no value: no id, or no ids.
    private static string NoneOrList(FieldMetadata field) => field.Interface == FieldInterface.Files ? "[]" : None;

    // A stored NULL and an empty list are the same value: a Files field holds no ids either way.
    private static string Canonical(FieldInterface kind, object? stored) => stored switch
    {
        null => kind == FieldInterface.Files ? "[]" : None,
        IEnumerable<Guid> ids => List(ids.Select(id => id.ToString())),
        _ => stored.ToString() ?? NotAnId,
    };

    private static string Canonical(FieldInterface kind, JsonElement sent) => kind == FieldInterface.Files
        ? FilesOf(sent)
        : sent.ValueKind switch
        {
            JsonValueKind.Null => None,
            JsonValueKind.String => IdOf(sent.GetString()),
            _ => NotAnId,
        };

    private static string FilesOf(JsonElement sent)
    {
        if (sent.ValueKind == JsonValueKind.Null) return "[]";
        if (sent.ValueKind != JsonValueKind.Array) return NotAnId;
        return List(sent.EnumerateArray().Select(e => e.ValueKind == JsonValueKind.String ? IdOf(e.GetString()) : NotAnId));
    }

    private static string IdOf(string? text) => Guid.TryParse(text, out var id) ? id.ToString() : NotAnId;

    private static string List(IEnumerable<string> ids) => "[" + string.Join(",", ids) + "]";
}
