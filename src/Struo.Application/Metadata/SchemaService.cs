// src/Struo.Application/Metadata/SchemaService.cs
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Metadata;

// Thin use-case seam over IMetadataProvider. Role-based field filtering is out of scope here;
// see RbacPermissionService for the current (collection-level, not field-level) RBAC surface.
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
    /// <see cref="CollectionMetadata.Translation"/>.Fields is a second, separate raw list of
    /// translatable field NAMES (the values themselves are redacted at read time by
    /// <c>TranslationOverlay</c>'s own hidden-translatable filter, not by this class) — the names
    /// still need filtering here too, or a Hidden+Translatable field (e.g. the sample's
    /// Article.InternalSlug) still surfaces its name under "translation.fields" even once removed
    /// from "fields" above.
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
