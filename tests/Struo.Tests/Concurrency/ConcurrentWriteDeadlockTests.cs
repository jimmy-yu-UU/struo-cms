using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Infrastructure.Revisions;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Concurrency;

/// <summary>
/// Concurrent updates of different items, and concurrent creates with many-to-many links, through the
/// REST stack. Every write must succeed and leave consistent translation, junction and revision rows.
/// On a live backend the writes run in parallel transactions; SQLite serializes writers.
/// </summary>
[Collection("ApiIntegration")]
public class ConcurrentWriteDeadlockTests(ApiFactory factory)
{
    private const int Workers = 16;
    private const int Rounds = 5;

    [Fact]
    public async Task Concurrent_updates_of_different_articles_all_succeed()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var marker = $"cu-{Guid.NewGuid():N}";
        var articles = new List<string>();
        for (var i = 0; i < Workers; i++)
            articles.Add(await CreateAsync(admin, "fxArticle",
                new { status = "draft", translations = new { en = new { title = $"{marker}-{i}-create" } } }));

        var failures = new ConcurrentBag<string>();
        for (var round = 0; round < Rounds; round++)
        {
            var r = round;
            await Task.WhenAll(articles.Select((id, worker) => Task.Run(async () =>
            {
                // Round 0 adds a second locale (the insert branch); later rounds update both rows.
                var body = new
                {
                    translations = new Dictionary<string, object>
                    {
                        ["en"] = new { title = $"{marker}-{worker}-{r}" },
                        ["zh-TW"] = new { title = $"{marker}-{worker}-zh-{r}" },
                    }
                };
                using var response = await admin.PutAsJsonAsync($"/api/items/fxArticle/{id}", body);
                if (response.StatusCode != HttpStatusCode.OK)
                    failures.Add($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            })));
        }

        failures.Should().BeEmpty();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var ids = articles.Select(Guid.Parse).ToList();
        var itemIds = articles.ToList();
        var translations = await db.Queryable<FxArticleTranslation>().Where(t => ids.Contains(t.FxArticleId)).ToListAsync();
        translations.Should().HaveCount(Workers * 2);
        for (var worker = 0; worker < Workers; worker++)
        {
            translations.Single(t => t.FxArticleId == ids[worker] && t.Locale == "en").Title
                .Should().Be($"{marker}-{worker}-{Rounds - 1}");
            translations.Single(t => t.FxArticleId == ids[worker] && t.Locale == "zh-TW").Title
                .Should().Be($"{marker}-{worker}-zh-{Rounds - 1}");
        }
        (await db.Queryable<Revision>()
                .Where(rv => rv.CollectionName == "fxArticle" && itemIds.Contains(rv.ItemId)).CountAsync())
            .Should().Be(Workers * (Rounds + 1));
    }

    [Fact]
    public async Task Concurrent_article_creates_with_tags_all_succeed_and_link_every_tag()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var marker = $"ct-{Guid.NewGuid():N}";
        var tags = new List<string>();
        for (var i = 0; i < 6; i++) tags.Add(await CreateAsync(admin, "fxTag", new { name = $"{marker}-tag-{i}" }));

        var failures = new ConcurrentBag<string>();
        var created = new ConcurrentDictionary<Guid, string[]>();
        for (var round = 0; round < Rounds; round++)
        {
            var r = round;
            await Task.WhenAll(Enumerable.Range(0, Workers).Select(worker => Task.Run(async () =>
            {
                var linked = new[] { tags[worker % 6], tags[(worker + 1) % 6], tags[(worker + 2) % 6] };
                var body = new
                {
                    status = "draft",
                    tags = linked,
                    translations = new { en = new { title = $"{marker}-{r}-{worker}" } }
                };
                using var response = await admin.PostAsJsonAsync("/api/items/fxArticle", body);
                var text = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.Created)
                {
                    failures.Add($"{(int)response.StatusCode}: {text}");
                    return;
                }
                using var doc = JsonDocument.Parse(text);
                created[Guid.Parse(doc.RootElement.GetProperty("data").GetProperty("id").GetString()!)] = linked;
            })));
        }

        failures.Should().BeEmpty();
        created.Should().HaveCount(Workers * Rounds);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var ids = created.Keys.ToList();
        var itemIds = ids.Select(id => id.ToString()).ToList();
        var links = await db.Queryable<FxArticleTag>().Where(l => ids.Contains(l.ArticleId)).ToListAsync();
        links.Should().HaveCount(ids.Count * 3);
        foreach (var (articleId, expected) in created)
            links.Where(l => l.ArticleId == articleId).Select(l => l.TagId.ToString()).Should()
                .BeEquivalentTo(expected);
        (await db.Queryable<FxArticleTranslation>().Where(t => ids.Contains(t.FxArticleId)).CountAsync())
            .Should().Be(ids.Count);
        (await db.Queryable<Revision>()
                .Where(rv => rv.CollectionName == "fxArticle" && itemIds.Contains(rv.ItemId)).CountAsync())
            .Should().Be(ids.Count);
    }

    private static async Task<string> CreateAsync(HttpClient client, string collection, object body)
    {
        using var response = await client.PostAsJsonAsync($"/api/items/{collection}", body);
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, text);
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("data").GetProperty("id").GetString()!;
    }
}
