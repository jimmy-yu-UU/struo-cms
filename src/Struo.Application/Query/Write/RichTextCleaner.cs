// src/Struo.Application/Query/Write/RichTextCleaner.cs
using Struo.Application.Security;
using Struo.Domain.Metadata.Enums;
using Struo.Domain.Metadata.Models;

namespace Struo.Application.Query;

/// <summary>
/// Server-side RichText normalization: sanitizes stored HTML and coerces visually-blank documents
/// to null. Shared by <see cref="ItemDeserializer"/> (parent fields) and
/// <see cref="ItemService"/>'s translation sync (per-locale fields).
/// </summary>
public sealed class RichTextCleaner(IHtmlSanitizer sanitizer)
{
    /// <summary>
    /// Sanitizes a RichText field value: null stays null; otherwise the HTML is run through the
    /// sanitizer and, if the cleaned result is visually blank (no text and no void media), coerced
    /// to null so blank editor documents (<c>&lt;p&gt;&lt;/p&gt;</c>) do not create dirty rows and
    /// so a required RichText field treats blank as missing.
    /// </summary>
    public string? Sanitize(string? raw)
    {
        if (raw is null) return null;
        var clean = sanitizer.Sanitize(raw);
        return IsBlankHtml(clean) ? null : clean;
    }

    private static bool IsBlankHtml(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return true;
        // Void/media content counts as non-blank.
        if (html.Contains("<img", StringComparison.OrdinalIgnoreCase) ||
            html.Contains("<hr", StringComparison.OrdinalIgnoreCase)) return false;
        // Strip tags and non-breaking spaces; blank if nothing meaningful remains.
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", string.Empty)
            .Replace("&nbsp;", " ", StringComparison.OrdinalIgnoreCase);
        return string.IsNullOrWhiteSpace(text);
    }

    public static bool IsRichTextField(CollectionMetadata meta, string fieldName) =>
        meta.Fields.Any(f =>
            string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase) &&
            f.Interface == FieldInterface.RichText);
}
