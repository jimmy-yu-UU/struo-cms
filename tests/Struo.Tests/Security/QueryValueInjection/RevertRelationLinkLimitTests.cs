// tests/Struo.Tests/Security/QueryValueInjection/RevertRelationLinkLimitTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Security.QueryValueInjection;

/// <summary>A revision snapshot is the system's own data: reverting to one is not bound by Query:MaxRelationLinks.</summary>
[Collection("ApiIntegration")]
public sealed class RevertRelationLinkLimitTests(ApiFactory factory)
{
    private const int Limit = 2;

    private static async Task<string> IdOf(HttpResponseMessage r)
    {
        var text = await r.Content.ReadAsStringAsync();
        r.IsSuccessStatusCode.Should().BeTrue(text);
        return JsonDocument.Parse(text).RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task Reverting_to_a_snapshot_with_more_links_than_the_limit_succeeds_and_restores_them()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var tags = new List<string>();
        for (var i = 0; i < Limit + 1; i++)
            tags.Add(await IdOf(await admin.PostAsJsonAsync("/api/items/fxTag", new { name = "rv-" + Guid.NewGuid().ToString("N")[..8] })));
        var article = await IdOf(await admin.PostAsJsonAsync("/api/items/fxArticle",
            new { status = "draft", tags, translations = new { en = new { title = "rv" } } }));
        (await admin.PutAsJsonAsync($"/api/items/fxArticle/{article}", new { tags = tags.Take(1).ToArray() }))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        using var small = factory.WithWebHostBuilder(b => b.ConfigureAppConfiguration((_, c) =>
            c.AddInMemoryCollection(new Dictionary<string, string?> { ["Query:MaxRelationLinks"] = Limit.ToString() })));
        var client = small.CreateClient();
        client.DefaultRequestHeaders.Add("X-Struo-CSRF", "1");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).StatusCode.Should().Be(HttpStatusCode.OK);

        var tooMany = await client.PutAsJsonAsync($"/api/items/fxArticle/{article}", new { tags });
        tooMany.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await tooMany.Content.ReadAsStringAsync()).Should().Contain("Query:MaxRelationLinks");

        var revert = await client.PostAsync($"/api/items/fxArticle/{article}/revisions/1/revert", null);
        revert.StatusCode.Should().Be(HttpStatusCode.OK, await revert.Content.ReadAsStringAsync());

        var db = small.Services.GetRequiredService<ISqlSugarClient>();
        var linked = (await db.Queryable<FxArticleTag>().Where(j => j.ArticleId == Guid.Parse(article)).ToListAsync())
            .Select(j => j.TagId).OrderBy(g => g).ToList();
        linked.Should().Equal(tags.Select(Guid.Parse).OrderBy(g => g));
    }
}
