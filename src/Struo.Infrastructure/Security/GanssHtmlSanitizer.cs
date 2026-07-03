using Ganss.Xss;
using Struo.Application.Security;

namespace Struo.Infrastructure.Security;

/// <summary>
/// Ganss.Xss-backed <see cref="IHtmlSanitizer"/>. The allowlist is configured once in the
/// constructor (tags/attributes/schemes) and never mutated afterward, so a single instance is
/// safe to share across requests. The allowlist mirrors the TipTap editor output (Phase 7f):
/// basic formatting + anchors (http/https/mailto) + relative-src images carrying data-file-id.
/// </summary>
public sealed class GanssHtmlSanitizer : Struo.Application.Security.IHtmlSanitizer
{
    private readonly HtmlSanitizer _sanitizer;

    public GanssHtmlSanitizer()
    {
        _sanitizer = new HtmlSanitizer();

        _sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
                 { "p", "h2", "h3", "strong", "em", "s", "ul", "ol", "li",
                   "blockquote", "pre", "code", "hr", "br", "a", "img" })
            _sanitizer.AllowedTags.Add(tag);

        _sanitizer.AllowedAttributes.Clear();
        foreach (var attr in new[] { "href", "src", "alt", "rel" })
            _sanitizer.AllowedAttributes.Add(attr);

        // data-file-id: allow data-* attributes (inert; carry no script surface).
        _sanitizer.AllowDataAttributes = true;

        _sanitizer.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto" })
            _sanitizer.AllowedSchemes.Add(scheme);

        // Drop inline styles and CSS entirely.
        _sanitizer.AllowedCssProperties.Clear();
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
