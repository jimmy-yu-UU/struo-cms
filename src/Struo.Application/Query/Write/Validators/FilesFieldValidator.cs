// src/Struo.Application/Query/Write/Validators/FilesFieldValidator.cs
using System.Reflection;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Files fields (List&lt;Guid&gt;) live on the parent entity as an ordered list of file ids. Drop
/// Guid.Empty, de-duplicate keeping first (preserves order), and enforce Required as a non-empty
/// list. No existence check — mirrors the scalar File/Image field (a deleted file degrades to a
/// raw-id fallback in the picker). A non-guid array element already 400s at the deserialize
/// guard. Non-translatable only.
/// </summary>
public sealed class FilesFieldValidator : IFieldValidator
{
    public void ValidateAndNormalize(FieldMetadata field, PropertyInfo pi, object entity)
    {
        var ids = (pi.GetValue(entity) as IEnumerable<Guid>) ?? [];
        var seen = new HashSet<Guid>();
        var cleaned = new List<Guid>();
        foreach (var id in ids)
        {
            if (id == Guid.Empty) continue;
            if (seen.Add(id)) cleaned.Add(id); // de-dup, keep first (preserves order)
        }
        if (field.Required && cleaned.Count == 0)
            throw new QueryException($"Field '{field.Name}' is required.");
        pi.SetValue(entity, cleaned);
    }
}
