using Ganss.Xss;
using Struo.Application.Security;

namespace Struo.Infrastructure.Security;

/// <summary>
/// Ganss.Xss-backed <see cref="IHtmlSanitizer"/>. The allowlist is configured once in the
/// constructor (tags/attributes/schemes) and never mutated afterward, so a single instance is
/// safe to share across requests. The allowlist mirrors the TipTap editor output:
/// basic formatting + anchors (http/https/mailto) + relative-src images carrying data-file-id,
/// plus basic tables, text-align/colour styles, and sub/superscript. Beyond stripping, it also
/// canonicalizes structure that TipTap cannot itself produce -- see <see cref="TableHeadNormalizer"/>
/// for the table-header-row case.
/// </summary>
public sealed class GanssHtmlSanitizer : Struo.Application.Security.IHtmlSanitizer
{
    private readonly HtmlSanitizer _sanitizer;

    public GanssHtmlSanitizer()
    {
        _sanitizer = new HtmlSanitizer();

        _sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
                 // h2-h6, deliberately not h1: the page title is the H1, so body content starts at
                 // H2. GanssHtmlSanitizerTests.Strips_h1_and_its_text pins that choice.
                 { "p", "h2", "h3", "h4", "h5", "h6", "strong", "em", "s", "ul", "ol", "li",
                   "blockquote", "pre", "code", "hr", "br", "a", "img",
                   // Basic tables + sub/superscript + the colour carrier tag.
                   "table", "thead", "tbody", "tr", "th", "td", "sub", "sup", "span" })
            _sanitizer.AllowedTags.Add(tag);

        _sanitizer.AllowedAttributes.Clear();
        foreach (var attr in new[] { "href", "src", "alt", "rel", "style" })
            _sanitizer.AllowedAttributes.Add(attr);

        // data-file-id: allow data-* attributes (inert; carry no script surface).
        _sanitizer.AllowDataAttributes = true;

        _sanitizer.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto" })
            _sanitizer.AllowedSchemes.Add(scheme);

        // The style attribute survives but carries exactly two CSS properties (colour + alignment);
        // Ganss strips every other property and dangerous values (url()/expression()) per-property.
        _sanitizer.AllowedCssProperties.Clear();
        _sanitizer.AllowedCssProperties.Add("color");
        _sanitizer.AllowedCssProperties.Add("text-align");
        _sanitizer.AllowedAtRules.Clear();

        // Harden every surviving anchor.
        _sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Html.Dom.IHtmlAnchorElement a)
            {
                a.SetAttribute("rel", "noopener noreferrer");
                a.RemoveAttribute("target");
            }
        };

        // Structural canonicalization, not stripping: see TableHeadNormalizer for why the header
        // row has to be sectioned server-side. It rides this event rather than a second parse
        // because the sanitizer has already built the DOM -- and rides this class rather than
        // RichTextCleaner because IHtmlSanitizer's only consumers in this repository are the two
        // RichTextCleaner instances ItemService builds, so no other caller is affected.
        _sanitizer.PostProcessDom += (_, e) => TableHeadNormalizer.Normalize(e.Document);
    }

    public string Sanitize(string html) => _sanitizer.Sanitize(html);
}
