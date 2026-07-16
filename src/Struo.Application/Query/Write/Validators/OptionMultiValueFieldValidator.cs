// src/Struo.Application/Query/Write/Validators/OptionMultiValueFieldValidator.cs
using System.Reflection;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Validates an option-bound multi-value field (MultiSelect / CheckboxGroup, a <c>List&lt;string&gt;</c>)
/// on the parent entity: rejects values not in the field's options, de-duplicates keeping first, and
/// enforces Required as a non-empty list.
/// </summary>
public sealed class OptionMultiValueFieldValidator : IFieldValidator
{
    public void ValidateAndNormalize(FieldMetadata field, PropertyInfo pi, object entity)
    {
        var values = (pi.GetValue(entity) as IEnumerable<string>) ?? [];
        var allowed = (field.Options ?? []).Select(o => o.Value).ToHashSet(StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var cleaned = new List<string>();
        foreach (var v in values)
        {
            if (v is null) continue;
            if (!allowed.Contains(v))
                throw new QueryException($"Field '{field.Name}' has value '{v}' not in its options.");
            if (seen.Add(v)) cleaned.Add(v); // de-dup, keep first
        }
        if (field.Required && cleaned.Count == 0)
            throw new QueryException($"Field '{field.Name}' is required.");
        pi.SetValue(entity, cleaned);
    }
}
