using AngleSharp.Dom;
using AngleSharp.Html.Dom;

namespace Struo.Infrastructure.Security;

/// <summary>
/// Moves a table's header row into a &lt;thead&gt; section, in place, on an already-parsed DOM.
///
/// Why this exists on the server rather than in the admin SPA: TipTap's table extension has no
/// thead node in its schema and its renderHTML is hard-coded to emit a single tbody, so the editor
/// cannot produce one. The stored HTML is what every fork's frontend renders, and the common
/// typography rules for tables key off `thead th`, so a header row left inside tbody renders with
/// no header treatment. Normalizing here covers every write path, including the ones that never go
/// through the editor at all.
///
/// The rule is deliberately narrow: only the first row, only when every one of its cells is a th,
/// and only when the table has no thead already. Anything else is content the author shaped
/// deliberately and is left exactly as it arrived.
/// </summary>
internal static class TableHeadNormalizer
{
    public static void Normalize(IHtmlDocument document)
    {
        // AngleSharp's QuerySelectorAll returns a non-live NodeList (per its own XML doc and the
        // DOM spec's static-NodeList requirement for this method), so the in-loop moves below
        // cannot disturb the iteration on their own. ToList is belt-and-braces at this size, not
        // a fix for a live-collection hazard that doesn't exist here.
        foreach (var table in document.QuerySelectorAll("table").ToList())
        {
            // Direct children throughout, never QuerySelector: a nested table's thead/tbody would
            // satisfy a descendant query on the OUTER table and make it skip its own header row,
            // or make it move a row out of the inner table. Nested tables are rare in CMS content
            // but the failure would be silent data reshaping, which is the worst kind here.
            if (table.Children.Any(c => c.LocalName == "thead")) continue;

            var body = table.Children.FirstOrDefault(c => c.LocalName == "tbody");
            if (body is null) continue;

            // Children, not descendants: a nested table's rows must not be mistaken for this
            // table's. Element children only -- the whitespace between rows in real stored HTML
            // arrives as text nodes.
            var firstRow = body.Children.FirstOrDefault(c => c.LocalName == "tr");
            if (firstRow is null) continue;

            var cells = firstRow.Children.Where(c => c.LocalName is "th" or "td").ToList();
            if (cells.Count == 0 || cells.Any(c => c.LocalName != "th")) continue;

            var head = document.CreateElement("thead");
            head.AppendChild(firstRow);           // AppendChild moves the node, it does not copy.
            table.InsertBefore(head, body);

            // A header-only table would otherwise keep an empty <tbody></tbody> in every fork's
            // stored content.
            if (!body.Children.Any(c => c.LocalName == "tr")) body.Remove();
        }
    }
}
