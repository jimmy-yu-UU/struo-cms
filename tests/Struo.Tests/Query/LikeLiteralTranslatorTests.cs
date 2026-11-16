// tests/Struo.Tests/Query/LikeLiteralTranslatorTests.cs
using AwesomeAssertions;
using SqlSugar;
using Struo.Domain.Query;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// Several string conditions in one statement: an implicit search over an own field and two translated
/// fields, an <c>_or</c> and an <c>_and</c> of string leaves. Every value is matched as plain text and no
/// two conditions share a parameter name.
/// </summary>
public sealed class LikeLiteralTranslatorTests : IDisposable
{
    private static readonly string[] SearchFields = ["code", "title", "subtitle"];

    private readonly TranslatableSearchHarness _h = new();
    private readonly Dictionary<string, Guid> _ids = [];

    public LikeLiteralTranslatorTests()
    {
        Add("own-code", "a_c", "zzz", null);
        Add("own-title", "zzz", "xa_cx", null);
        Add("own-subtitle", "zzz", "zzz", "a_c");
        Add("wildcard-lookalike", "abc", "abc", "axc");
        Add("percent", "100%", "zzz", null);
        Add("plain", "zzz", "zzz", null);
    }

    public void Dispose() => _h.Dispose();

    private void Add(string key, string code, string title, string? subtitle)
    {
        var id = Guid.NewGuid();
        _ids[key] = id;
        _h.Db.Insertable(new TsItem { Id = id, Code = code }).ExecuteCommand();
        _h.Db.Insertable(new TsItemTranslation { TsItemId = id, Locale = "en", Title = title, Subtitle = subtitle }).ExecuteCommand();
    }

    private static ComparisonFilter Leaf(string field, QueryOperator op, string value) => new(field, op, value);

    private List<string> Keys(List<IConditionalModel> conds) =>
        _h.Db.Queryable<TsItem>().Where(conds).ToList()
            .Select(i => _ids.Single(p => p.Value == i.Id).Key).Order().ToList();

    private List<IConditionalModel> Translate(FilterNode? filter, string? search = null) =>
        _h.Translator.Translate("tsItem", filter, search, null, search is null ? [] : SearchFields, "en");

    [Fact]
    public void A_search_over_an_own_and_two_translated_fields_matches_the_text_in_any_of_them()
    {
        var conds = Translate(null, "a_c");
        Keys(conds).Should().Equal("own-code", "own-subtitle", "own-title");
        _h.Db.Queryable<TsItem>().Where(conds).ToSql().Value.Select(p => p.ParameterName).Should().OnlyHaveUniqueItems();
        Keys(Translate(null, "00%")).Should().Equal("percent");
    }

    [Fact]
    public void An_or_of_two_string_leaves_matches_each_as_plain_text()
    {
        var filter = new LogicalFilter(LogicalOperator.Or,
            [Leaf("code", QueryOperator.Contains, "a_c"), Leaf("code", QueryOperator.StartsWith, "100%")]);
        var conds = Translate(filter);
        Keys(conds).Should().Equal("own-code", "percent");
        _h.Db.Queryable<TsItem>().Where(conds).ToSql().Value.Select(p => p.ParameterName).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void An_or_of_an_own_leaf_and_a_translated_leaf_matches_each_as_plain_text()
    {
        var filter = new LogicalFilter(LogicalOperator.Or,
            [Leaf("code", QueryOperator.Contains, "a_c"), Leaf("title", QueryOperator.Contains, "a_c"), Leaf("subtitle", QueryOperator.EndsWith, "_c")]);
        Keys(Translate(filter)).Should().Equal("own-code", "own-subtitle", "own-title");
    }

    [Fact]
    public void An_and_of_two_string_leaves_matches_each_as_plain_text()
    {
        var filter = new LogicalFilter(LogicalOperator.And,
            [Leaf("code", QueryOperator.Contains, "a_"), Leaf("code", QueryOperator.EndsWith, "_c"), Leaf("title", QueryOperator.Contains, "zzz")]);
        var conds = Translate(filter);
        Keys(conds).Should().Equal("own-code");
        _h.Db.Queryable<TsItem>().Where(conds).ToSql().Value.Select(p => p.ParameterName).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void A_translated_field_and_an_own_field_in_one_and_keep_their_values_apart()
    {
        var filter = new LogicalFilter(LogicalOperator.And,
            [Leaf("title", QueryOperator.Contains, "a_c"), Leaf("subtitle", QueryOperator.Contains, "_")]);
        Keys(Translate(filter)).Should().BeEmpty();
        var either = new LogicalFilter(LogicalOperator.And,
            [Leaf("code", QueryOperator.Contains, "_"), Leaf("title", QueryOperator.Contains, "zzz")]);
        Keys(Translate(either)).Should().Equal("own-code");
    }
}
