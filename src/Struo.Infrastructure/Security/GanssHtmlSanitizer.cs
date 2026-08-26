using System.Text.RegularExpressions;
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
/// for the table-header-row case. An anchor's <c>rel</c> is never taken from the input: it is
/// derived from <c>target</c> (a surviving anchor whose <c>target="_blank"</c> and whose <c>href</c>
/// was itself kept gets exactly <c>rel="noopener"</c>; anything else gets neither), so the one
/// writer of that attribute is this class, not whatever produced the HTML. The same reasoning
/// applies to <c>width</c>: <see cref="HtmlSanitizer.AllowedAttributes"/> has no per-tag concept,
/// it is a single global set of attribute names, so an attribute that should only mean something on
/// one tag has to be narrowed in this handler, not in that list. An image's <c>width</c> survives
/// only when it is a bare positive integer with no leading zero -- the stored contract is a plain
/// pixel count and nothing else, never a percentage, a decimal, or a value carrying a unit; every
/// other element loses it unconditionally, allowlisted or not. <c>height</c> is never allowlisted
/// at all -- a fork's
/// front end is not guaranteed to pair a stored width with CSS <c>height: auto</c>, and shipping
/// both dimensions into a renderer that only caps <c>max-width: 100%</c> would stretch the box
/// the browser lays out while the image itself is scaled to fit the width, squashing its aspect
/// ratio.
/// </summary>
public sealed class GanssHtmlSanitizer : Struo.Application.Security.IHtmlSanitizer
{
    // One to five ASCII digits, no leading zero: the only shape a bare pixel count can take, and
    // nothing else -- not "0" (meaningless as a width), not a percentage or any other unit, not a
    // decimal, and not padded with leading/trailing whitespace. Anchored with \A and \z rather than
    // ^ and $: in .NET, $ (without RegexOptions.Multiline) matches at end-of-string OR immediately
    // before a single trailing '\n', so "480\n" -- reachable via an attribute value decoded from
    // "480&#10;" -- would satisfy ^[1-9][0-9]{0,4}$ despite not being the bare digits the contract
    // promises. \A and \z have no such exemption on either end (verified directly against the .NET
    // regex engine: "480\n" no longer matches, "480" still does, and a leading "\n480" is rejected
    // the same way under both anchor pairs, so \z is the only end that actually changes here). Five
    // digits caps out at 99999px, comfortably above any real editor image while still rejecting
    // pathological input. Pre-compiled and reused across every node this handler visits, rather
    // than constructed per call.
    private static readonly Regex AllowedWidthPattern =
        new(@"\A[1-9][0-9]{0,4}\z", RegexOptions.Compiled, TimeSpan.FromMilliseconds(100));

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
        // width is global here (see the class summary) and narrowed to <img>-only, integer-only
        // in PostProcessNode below; height is deliberately never added.
        foreach (var attr in new[] { "href", "src", "alt", "rel", "target", "style", "width" })
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

        // Harden every surviving anchor: rel is derived from target, never taken from the input.
        // An un-allowlisted target never reaches this handler at all -- remove "target" from
        // AllowedAttributes above and Blank_target_survives_with_exactly_rel_noopener fails, but that
        // test alone does not pin the ordering claim below: it fails the same way under either event
        // ordering, so it only proves the allowlist entry is load-bearing. What actually pins
        // PostProcessNode firing after attribute filtering (same as PostProcessDom, RT-3) is
        // Anchor_with_rejected_href_does_not_keep_target: that test's href carries a rejected scheme,
        // so it only passes if attribute filtering has already stripped it by the time
        // HasAttribute("href") below is evaluated.
        //
        // Only an exact, case-sensitive "_blank" is an opt-in, and only alongside a surviving href
        // (an anchor with no href, whether because none was given or because its href was itself
        // rejected -- href is stripped attribute-by-attribute, not by discarding the whole element --
        // is not a link and gets no link attributes). Anything else, including an absent target, is
        // treated as same-tab and gets neither attribute. This narrows the previous absolute strip
        // rather than replacing it: the reverse-tabnabbing gap the old strip closed was a
        // target="_blank" without a rel to go with it, and that gap cannot reopen here because
        // rel="noopener" is set on exactly the links that keep target="_blank" -- never on any other.
        //
        // Every other allowlisted tag survives without a handler at all, so a target left on one of
        // them (e.g. <p target="_blank">) is inert today, but "target" being allowlisted globally
        // would let it ride through the day AllowedTags grows a tag that gives it meaning -- so it is
        // stripped from every element except the anchor, unconditionally.
        //
        // width gets the same global-allowlist -> per-tag narrowing treatment (see the class summary
        // for why), but it is a second, independent concern from target above: an <img> is not an
        // IHtmlAnchorElement and an <a> is not an IHtmlImageElement, so this is a second top-level
        // "if / else if" pair rather than another branch threaded into the one above. Chaining it
        // onto the existing if/else-if as more branches of the *same* chain would make the two
        // concerns mutually exclusive per node -- an <img> would never reach a width check because
        // it already satisfied the "not an anchor" branch and stopped there, and conversely giving
        // <a> a width branch would require threading it through the anchor branch and disturbing the
        // rel/target logic that branch owns. Two separate statements keep them orthogonal: every
        // node is tested against both, in either order, with neither able to make the other's branch
        // unreachable.
        _sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is AngleSharp.Html.Dom.IHtmlAnchorElement a)
            {
                if (a.GetAttribute("target") == "_blank" && a.HasAttribute("href"))
                {
                    a.SetAttribute("rel", "noopener");
                }
                else
                {
                    a.RemoveAttribute("target");
                    a.RemoveAttribute("rel");
                }
            }
            else if (e.Node is AngleSharp.Dom.IElement nonAnchor)
            {
                nonAnchor.RemoveAttribute("target");
            }

            if (e.Node is AngleSharp.Html.Dom.IHtmlImageElement img)
            {
                var width = img.GetAttribute("width");
                if (width is null || !AllowedWidthPattern.IsMatch(width))
                {
                    img.RemoveAttribute("width");
                }
            }
            else if (e.Node is AngleSharp.Dom.IElement nonImage)
            {
                nonImage.RemoveAttribute("width");
            }
        };

        // Structural canonicalization, not stripping: see TableHeadNormalizer for why the header
        // row has to be sectioned server-side. It rides this event rather than a second parse
        // because the sanitizer has already built the DOM -- and rides this class rather than
        // RichTextCleaner because this adapter *is* the RichText pipeline's sanitizer, not a
        // second stage bolted onto it: a consumer added elsewhere would opt into normalization
        // along with stripping, which IHtmlSanitizer's own interface doc now states plainly.
        _sanitizer.PostProcessDom += (_, e) => TableHeadNormalizer.Normalize(e.Document);
    }

    public string Sanitize(string html) => _sanitizer.Sanitize(html);
}
