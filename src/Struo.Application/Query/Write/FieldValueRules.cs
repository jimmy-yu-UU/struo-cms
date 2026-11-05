// src/Struo.Application/Query/Write/FieldValueRules.cs
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Shared Required / MaxLength primitives for the write path. The parent-field variants are called
/// from <see cref="ItemDeserializer"/>, the per-locale translation variants from
/// <see cref="ItemWriteSideSync"/>.
/// </summary>
internal static class FieldValueRules
{
    internal static bool IsMissing(object? value) =>
        value is null
        || (value is string s && string.IsNullOrWhiteSpace(s))
        // A non-nullable Guid FK (e.g. Permission.RoleId, UserRole.UserId/RoleId) can never be
        // null, so without this an omitted Required Guid field would silently bind to its CLR
        // default and insert a dangling row. Scoped to Guid only — 0 and other falsy scalars stay
        // legal values for a Required field.
        || (value is Guid g && g == Guid.Empty);

    internal static void RequireParent(string fieldName, object? value)
    {
        if (IsMissing(value))
            throw new QueryException($"Field '{fieldName}' is required.");
    }

    internal static bool IsTranslationMissing(bool present, object? value) => !present || IsMissing(value);

    internal static QueryErrorDetail MissingTranslationDetail(string fieldName, string locale) =>
        new(fieldName, locale, $"Required translation field '{fieldName}' is missing for locale '{locale}'.");

    internal static QueryException MissingTranslationsError(IReadOnlyList<QueryErrorDetail> details) =>
        new(details[0].Message, details);

    internal static void CheckMaxLengthParent(string fieldName, int max, string? value)
    {
        if (value is not null && value.Length > max)
            throw new QueryException($"Field '{fieldName}' exceeds maximum length {max}.");
    }

    internal static void CheckMaxLengthTranslation(string fieldName, int max, string locale, string? value)
    {
        if (value is null || value.Length <= max) return;
        var message = $"Field '{fieldName}' exceeds maximum length {max} for locale '{locale}'.";
        throw new QueryException(message, [new QueryErrorDetail(fieldName, locale, message)]);
    }
}
