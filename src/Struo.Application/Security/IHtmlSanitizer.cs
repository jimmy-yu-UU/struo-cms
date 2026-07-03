namespace Struo.Application.Security;

/// <summary>
/// Sanitizes untrusted HTML (rich-text field values) before persistence, removing
/// scripts, event handlers, and unsafe URLs while preserving an allowlist of
/// formatting tags. Implementations MUST be safe to call concurrently.
/// </summary>
public interface IHtmlSanitizer
{
    /// <summary>Returns a cleaned copy of <paramref name="html"/> containing only allowlisted markup.</summary>
    string Sanitize(string html);
}
