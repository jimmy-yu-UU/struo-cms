// src/Struo.Application/Query/Write/FieldFormatRules.cs
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Query;

/// <summary>
/// Format rules for Email, Url and Select/Radio values, defined once for root fields, translation
/// values and Repeater sub-fields. <see cref="Violation"/> returns the predicate of the error message
/// (so each caller can wrap it in its own field/locale/row prefix) or null when the value passes.
/// </summary>
internal static class FieldFormatRules
{
    internal const string EmailPhrase = "must be a valid email address";
    internal const string UrlPhrase = "must be a valid http or https URL";

    internal static bool IsChecked(FieldInterface fieldInterface) =>
        fieldInterface is FieldInterface.Email or FieldInterface.Url
            or FieldInterface.Select or FieldInterface.Radio;

    /// <summary>
    /// The violated rule's phrase, or null. Null and empty values are not checked; a caller that also
    /// treats whitespace-only text as empty skips those before calling.
    /// </summary>
    internal static string? Violation(FieldInterface fieldInterface, IReadOnlyList<FieldOption>? options, string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;
        return fieldInterface switch
        {
            FieldInterface.Email => IsEmail(value) ? null : EmailPhrase,
            FieldInterface.Url => IsHttpUrl(value) ? null : UrlPhrase,
            FieldInterface.Select or FieldInterface.Radio => IsOption(options, value)
                ? null
                : $"value '{value}' is not in its options",
            _ => null,
        };
    }

    internal static bool IsEmail(string value)
    {
        if (value.Any(char.IsWhiteSpace)) return false;
        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@')) return false;
        var domain = value.AsSpan(at + 1);
        for (var i = 1; i < domain.Length - 1; i++)
            if (domain[i] == '.') return true;
        return false;
    }

    internal static bool IsHttpUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.Host.Length > 0;

    private static bool IsOption(IReadOnlyList<FieldOption>? options, string value) =>
        options is not { Count: > 0 }
        || options.Any(o => string.Equals(o.Value, value, StringComparison.Ordinal));
}
