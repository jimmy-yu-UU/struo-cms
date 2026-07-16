// src/Struo.Application/Query/Write/FieldValueRules.cs
using Struo.Domain.Query;

namespace Struo.Application.Query;

/// <summary>
/// Shared Required / MaxLength primitives for the write path, deduplicating the parent-field
/// (<see cref="ItemDeserializer"/>) and per-locale translation (<see cref="ItemService"/>) variants.
/// Exception message strings are byte-for-byte identical to the inline checks they replaced.
/// </summary>
internal static class FieldValueRules
{
    internal static bool IsMissing(object? value) =>
        value is null || (value is string s && string.IsNullOrWhiteSpace(s));

    internal static void RequireParent(string fieldName, object? value)
    {
        if (IsMissing(value))
            throw new QueryException($"Field '{fieldName}' is required.");
    }

    internal static void RequireTranslation(string fieldName, string locale, bool present, object? value)
    {
        if (!present || IsMissing(value))
            throw new QueryException(
                $"Required translation field '{fieldName}' is missing for locale '{locale}'.");
    }

    internal static void CheckMaxLengthParent(string fieldName, int max, string? value)
    {
        if (value is not null && value.Length > max)
            throw new QueryException($"Field '{fieldName}' exceeds maximum length {max}.");
    }

    internal static void CheckMaxLengthTranslation(string fieldName, int max, string locale, string? value)
    {
        if (value is not null && value.Length > max)
            throw new QueryException(
                $"Field '{fieldName}' exceeds maximum length {max} for locale '{locale}'.");
    }
}
