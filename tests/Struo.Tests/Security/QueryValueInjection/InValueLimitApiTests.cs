// tests/Struo.Tests/Security/QueryValueInjection/InValueLimitApiTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Security.QueryValueInjection;

/// <summary>`Query:MaxInValues` counts the values of every `_in` and `_nin`, the root filter and `deep` together.</summary>
[Collection("ApiIntegration")]
public sealed class InValueLimitApiTests(ApiFactory factory)
{
    private static string Csv(int n, string prefix = "v") => string.Join(",", Enumerable.Range(0, n).Select(i => $"{prefix}{i}"));

    private static string[] Words(int n, string prefix = "v") => Enumerable.Range(0, n).Select(i => $"{prefix}{i}").ToArray();

    private static async Task<(HttpStatusCode Status, string Body)> PostQueryAsync(HttpClient c, object envelope)
    {
        var r = await c.PostAsync("/api/items/fxCategory/query",
            new StringContent(JsonSerializer.Serialize(envelope), Encoding.UTF8, "application/json"));
        return (r.StatusCode, await r.Content.ReadAsStringAsync());
    }

    private static void ShouldNameTheLimit((HttpStatusCode Status, string Body) r)
    {
        r.Status.Should().Be(HttpStatusCode.BadRequest, r.Body);
        r.Body.Should().Contain("Query:MaxInValues").And.Contain("500");
    }

    [Fact]
    public async Task A_query_string_list_counts_its_comma_separated_elements()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var over = await c.GetAsync("/api/items/fxCategory?filter[name][_in]=" + Uri.EscapeDataString(Csv(501)));
        ShouldNameTheLimit((over.StatusCode, await over.Content.ReadAsStringAsync()));

        var ok = await c.GetAsync("/api/items/fxCategory?filter[name][_in]=" + Uri.EscapeDataString(Csv(500)));
        ok.StatusCode.Should().Be(HttpStatusCode.OK);
        var nin = await c.GetAsync("/api/items/fxCategory?filter[name][_nin]=" + Uri.EscapeDataString(Csv(501)));
        ShouldNameTheLimit((nin.StatusCode, await nin.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task A_json_list_counts_its_elements_and_in_and_nin_add_up()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        ShouldNameTheLimit(await PostQueryAsync(c, new { filter = new { name = new { _in = Words(501) } } }));
        (await PostQueryAsync(c, new { filter = new { name = new { _in = Words(500) } } })).Status.Should().Be(HttpStatusCode.OK);
        ShouldNameTheLimit(await PostQueryAsync(c, new { filter = new { name = new { _in = Words(300), _nin = Words(201, "w") } } }));
        (await PostQueryAsync(c, new { filter = new { name = new { _in = Words(300), _nin = Words(200, "w") } } })).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Elements_of_a_list_with_commas_inside_count_once_each()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var commas = Enumerable.Range(0, 500).Select(i => $"a,{i}").ToArray();
        (await PostQueryAsync(c, new { filter = new { name = new { _in = commas } } })).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Deep_filters_share_the_budget_with_the_root_filter()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        ShouldNameTheLimit(await PostQueryAsync(c, new
        {
            filter = new { name = new { _in = Words(300) } },
            deep = new { children = new { filter = new { name = new { _in = Words(201, "d") } } } },
        }));
        ShouldNameTheLimit(await PostQueryAsync(c, new
        {
            deep = new { children = new { filter = new { name = new { _in = Words(501) } } } },
        }));
        (await PostQueryAsync(c, new
        {
            filter = new { name = new { _in = Words(300) } },
            deep = new { children = new { filter = new { name = new { _in = Words(200, "d") } } } },
        })).Status.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Relation_predicates_count_toward_the_same_budget()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        ShouldNameTheLimit(await PostQueryAsync(c, new
        {
            filter = new { name = new { _in = Words(300) }, children = new { _some = new { name = new { _in = Words(201, "p") } } } },
        }));
    }

    [Fact]
    public async Task Graphql_runs_the_same_check()
    {
        var anon = factory.CreateClient();
        string Query(int n) =>
            "{ languages(filter: { code: { in: [" + string.Join(",", Words(n).Select(w => $"\"{w}\"")) + "] } }) { items { id } } }";

        var over = await anon.PostAsJsonAsync("/graphql", new { query = Query(501) });
        var overBody = await over.Content.ReadAsStringAsync();
        overBody.Should().Contain("\"errors\"").And.Contain("Query:MaxInValues");

        var ok = await anon.PostAsJsonAsync("/graphql", new { query = Query(500) });
        (await ok.Content.ReadAsStringAsync()).Should().NotContain("\"errors\"");
    }

    [Fact]
    public async Task An_empty_query_string_list_counts_nothing()
    {
        var c = await factory.CreateAuthenticatedClientAsync();
        var r = await c.GetAsync("/api/items/fxCategory?filter[name][_in]=");
        r.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
