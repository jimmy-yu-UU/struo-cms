// src/Struo.Application/Query/Write/Validators/RepeaterFieldValidator.cs
using System.Reflection;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Repeater fields (List&lt;TChild&gt;) live on the parent entity as an ordered list of typed child
/// objects. Drop fully-blank rows (every sub-field null or whitespace string), enforce each
/// kept row's sub-field Required / Select-Radio option membership / MaxLength, and enforce the
/// parent Required as a non-empty list. Non-translatable only. A wrong-shaped element already
/// 400s at the deserialize guard.
/// </summary>
public sealed class RepeaterFieldValidator : IFieldValidator
{
    public void ValidateAndNormalize(FieldMetadata field, PropertyInfo pi, object entity)
    {
        if (field.Fields is null) return;
        if (pi.GetValue(entity) is not System.Collections.IEnumerable rowsEnum) return;

        var cleaned = (System.Collections.IList)Activator.CreateInstance(pi.PropertyType)!;

        var rowIndex = 0;
        foreach (var row in rowsEnum)
        {
            rowIndex++;
            if (row is null) continue;

            // Blank-row detection: every sub-field is null or a whitespace-only string.
            var allBlank = true;
            foreach (var sub in field.Fields)
            {
                var v = PropertyAccessorCache.Read(row, sub.Name);
                if (v is null) continue;
                if (v is string sv && string.IsNullOrWhiteSpace(sv)) continue;
                allBlank = false; break;
            }
            if (allBlank) continue;

            foreach (var sub in field.Fields)
            {
                var v = PropertyAccessorCache.Read(row, sub.Name);
                var sv = v as string;

                if (sub.Required && (v is null || (sv is not null && string.IsNullOrWhiteSpace(sv))))
                    throw new QueryException(
                        $"Repeater field '{field.Name}' row {rowIndex}: '{sub.Name}' is required.");

                if (sv is not null && sub.MaxLength is > 0 && sv.Length > sub.MaxLength.Value)
                    throw new QueryException(
                        $"Repeater field '{field.Name}' row {rowIndex}: '{sub.Name}' exceeds maximum length {sub.MaxLength}.");

                if (sv is not null && sub.Options is { Count: > 0 } &&
                    !string.IsNullOrEmpty(sv) &&
                    !sub.Options.Any(o => string.Equals(o.Value, sv, StringComparison.Ordinal)))
                    throw new QueryException(
                        $"Repeater field '{field.Name}' row {rowIndex}: '{sub.Name}' value '{sv}' is not in its options.");
            }
            cleaned.Add(row);
        }

        if (field.Required && cleaned.Count == 0)
            throw new QueryException($"Field '{field.Name}' is required.");
        pi.SetValue(entity, cleaned);
    }
}
