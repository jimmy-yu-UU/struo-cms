namespace Struo.Application.Search;

/// <param name="Collection">Collection name as the query DSL sees it (e.g. <c>article</c>).</param>
/// <param name="Term">The <c>search=</c> term verbatim — not trimmed, not lower-cased.</param>
/// <param name="Locale">Effective query locale (explicit <c>locale=</c> or the site default).</param>
/// <param name="SearchableFields">Core's <c>Searchable &amp;&amp; !Hidden</c> field names — a hint only; a provider may index other fields.</param>
public sealed record SearchRequest(string Collection, string Term, string Locale, IReadOnlyList<string> SearchableFields);
