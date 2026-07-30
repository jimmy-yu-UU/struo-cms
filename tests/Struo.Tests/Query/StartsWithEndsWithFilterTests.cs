using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Query;

/// <summary>
/// Regression lock for the review-flagged `_starts_with`/`_ends_with` mapping fix in
/// ConditionalModelTranslator.MapOperator. Before the fix,
/// `_starts_with` mapped to SqlSugar's LikeRight (silently matching a SUFFIX) and
/// `_ends_with` mapped to LikeLeft (silently matching a PREFIX) — exactly backwards.
///
/// Each test below seeds two rows whose names share a token that sits at the START of
/// one row and the END of the other, then asserts the operator returns ONLY the row
/// matching in the correct direction. If the mapping were swapped back, the wrong row
/// would match instead and the assertion would fail — see per-test comments for the
/// concrete swapped-mapping failure mode.
/// </summary>
[Collection("ApiIntegration")]
public class StartsWithEndsWithFilterTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;
    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private async Task<string> Post(System.Net.Http.HttpClient c, string col, object body) =>
        Root(await (await c.PostAsJsonAsync($"/api/items/{col}", body)).Content.ReadAsStringAsync())
            .GetProperty("data").GetProperty("id").GetString()!;

    [Fact]
    public async Task StartsWith_matches_prefix_row_not_suffix_row()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        const string token = "8c3bstartmark";
        // prefixRow starts with the token; suffixRow ends with the token but does not
        // start with it. A correct _starts_with (value%) must return only prefixRow.
        // Under the pre-fix swapped mapping (_starts_with => LikeRight = %value), this
        // would instead match suffixRow (which does end with the token) and the
        // assertion below would fail.
        var prefixRow = await Post(c, "category", new { name = $"{token}-prefix-row" });
        var suffixRow = await Post(c, "category", new { name = $"suffix-row-{token}" });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new
            {
                id = new Dictionary<string, object> { ["_in"] = new[] { prefixRow, suffixRow } },
                name = new Dictionary<string, object> { ["_starts_with"] = token }
            }
        });
        var resp = await c.PostAsJsonAsync("/api/items/category/query", envelope);
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        var ids = data.EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();

        ids.Should().ContainSingle().Which.Should().Be(prefixRow);
    }

    [Fact]
    public async Task EndsWith_matches_suffix_row_not_prefix_row()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        const string token = "8c3bendmark";
        // suffixRow ends with the token; prefixRow starts with the token but does not
        // end with it. A correct _ends_with (%value) must return only suffixRow.
        // Under the pre-fix swapped mapping (_ends_with => LikeLeft = value%), this
        // would instead match prefixRow (which does start with the token) and the
        // assertion below would fail.
        var prefixRow = await Post(c, "category", new { name = $"{token}-prefix-row" });
        var suffixRow = await Post(c, "category", new { name = $"suffix-row-{token}" });

        var envelope = JsonSerializer.SerializeToElement(new
        {
            filter = new
            {
                id = new Dictionary<string, object> { ["_in"] = new[] { prefixRow, suffixRow } },
                name = new Dictionary<string, object> { ["_ends_with"] = token }
            }
        });
        var resp = await c.PostAsJsonAsync("/api/items/category/query", envelope);
        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data");
        var ids = data.EnumerateArray().Select(r => r.GetProperty("id").GetString()).ToList();

        ids.Should().ContainSingle().Which.Should().Be(suffixRow);
    }
}
