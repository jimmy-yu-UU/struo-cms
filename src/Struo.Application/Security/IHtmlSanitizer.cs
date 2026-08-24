namespace Struo.Application.Security;

/// <summary>
/// Sanitizes untrusted HTML (rich-text field values) before persistence, removing
/// scripts, event handlers, and unsafe URLs while preserving an allowlist of
/// formatting tags. An implementation may also canonicalize structure the source markup left
/// ambiguous (for example, sectioning an all-header first table row into a &lt;thead&gt;) --
/// this is normalization, not stripping, and callers should not assume the output is a strict
/// subset of the input tags. Implementations MUST be safe to call concurrently.
/// </summary>
public interface IHtmlSanitizer
{
    /// <summary>Returns a cleaned copy of <paramref name="html"/> containing only allowlisted markup.</summary>
    string Sanitize(string html);
}
