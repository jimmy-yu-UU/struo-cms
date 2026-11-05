// src/Struo.Application/Query/Write/Validators/FormatFieldValidator.cs
using System.Reflection;
using Struo.Domain.Metadata.Models;
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Email / Url / Select / Radio value check for a non-translatable field (see
/// <see cref="FieldFormatRules"/>). Whitespace-only text counts as empty, as it does for Required.
/// Translation values run the same rules in <c>ItemWriteSideSync</c>.
/// </summary>
public sealed class FormatFieldValidator : IFieldValidator
{
    public void ValidateAndNormalize(FieldMetadata field, PropertyInfo pi, object entity)
    {
        if (pi.GetValue(entity) is not string value || FieldValueRules.IsMissing(value)) return;
        if (FieldFormatRules.Violation(field.Interface, field.Options, value) is { } phrase)
            throw new QueryException($"Field '{field.Name}' {phrase}.");
    }
}
