using Ganss.Xss;
using Struo.Application.Security;

namespace Struo.Infrastructure.Security;

/// <summary>
/// Ganss.Xss-backed <see cref="IHtmlSanitizer"/>. The allowlist is configured once in the
/// constructor (tags/attributes/schemes) and never mutated afterward, so a single instance is
/// safe to share across requests. The allowlist mirrors the TipTap editor output:
/// basic formatting + anchors (http/https/mailto) + relative-src images carrying data-file-id,
/// plus basic tables, text-align/colour styles, and sub/superscript.
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
    }

    public string Sanitize(string html) => _sanitizer.Sanitize(html);
}
