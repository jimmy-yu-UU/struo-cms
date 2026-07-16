// src/Struo.Application/Query/Write/Validators/TagsFieldValidator.cs
using System.Reflection;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Validates a Tags field (<c>List&lt;TagItem&gt;</c>) on the parent entity: rejects blank tag
/// values, de-duplicates by value keeping first, coerces blank labels to null, and enforces Required
/// as a non-empty list.
/// </summary>
public sealed class TagsFieldValidator : IFieldValidator
{
    public void ValidateAndNormalize(FieldMetadata field, PropertyInfo pi, object entity)
    {
        var tags = (pi.GetValue(entity) as IEnumerable<TagItem>) ?? [];
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cleaned = new List<TagItem>();
        foreach (var t in tags)
        {
            if (t is null || string.IsNullOrWhiteSpace(t.Value))
                throw new QueryException($"Field '{field.Name}' has a tag with an empty value.");
            if (!seen.Add(t.Value)) continue; // de-dup by value, keep first
            var label = string.IsNullOrWhiteSpace(t.Label) ? null : t.Label;
            cleaned.Add(new TagItem(t.Value, label));
        }
        if (field.Required && cleaned.Count == 0)
            throw new QueryException($"Field '{field.Name}' is required.");
        pi.SetValue(entity, cleaned);
    }
}
