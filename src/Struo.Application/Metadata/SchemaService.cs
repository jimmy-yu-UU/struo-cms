// src/Struo.Application/Metadata/SchemaService.cs
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Metadata;

// Thin use-case seam; Phase 6 adds role-based field filtering here.
public sealed class SchemaService(IMetadataProvider provider)
{
    public IReadOnlyList<CollectionMetadata> GetAll() => provider.GetCollections()
        .Select(WithoutHiddenFields).ToList();

    public CollectionMetadata? Get(string collection)
    {
        var meta = provider.GetCollection(collection);
        return meta is null ? null : WithoutHiddenFields(meta);
    }

    /// <summary>
    /// Strips <see cref="FieldMetadata.Hidden"/> fields before exposing metadata to callers.
    /// Hidden fields (e.g. Password, AccessToken) must never be projected or accepted by the
    /// generic CRUD path; removing them from the schema prevents UI/clients from discovering them.
    ///
    /// SEC-14: <see cref="CollectionMetadata.Translation"/>.Fields is a second, separate raw list of
    /// translatable field NAMES (values are already redacted elsewhere by SEC-13, but the names
    /// themselves leaked here) — it must be filtered the same way, or a Hidden+Translatable field
    /// (e.g. the sample's Article.InternalSlug) still surfaces its name under "translation.fields"
    /// even once removed from "fields" above.
    /// </summary>
    private static CollectionMetadata WithoutHiddenFields(CollectionMetadata meta)
    {
        var visibleFieldNames = meta.Fields.Where(f => !f.Hidden).Select(f => f.Name).ToHashSet();
        return meta with
        {
            Fields = meta.Fields.Where(f => !f.Hidden).ToList(),
            Translation = meta.Translation is { } tm
                ? tm with { Fields = tm.Fields.Where(visibleFieldNames.Contains).ToList() }
                : null,
        };
    }
}
