using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Infrastructure.Revisions;
using Struo.Sample.Blog;
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
            articles.Add(await CreateAsync(admin, "article",
                new { status = "draft", translations = new { en = new { title = $"{marker}-{i}-create" } } }));

        var failures = new ConcurrentBag<string>();
        for (var round = 0; round < Rounds; round++)
        {
            var r = round;
            await Task.WhenAll(articles.Select((id, worker) => Task.Run(async () =>
            {
                var body = new { translations = new { en = new { title = $"{marker}-{worker}-{r}" } } };
                using var response = await admin.PutAsJsonAsync($"/api/items/article/{id}", body);
                if (response.StatusCode != HttpStatusCode.OK)
                    failures.Add($"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
            })));
        }

        failures.Should().BeEmpty();

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var ids = articles.Select(Guid.Parse).ToList();
        var itemIds = articles.ToList();
        var translations = await db.Queryable<ArticleTranslation>().Where(t => ids.Contains(t.ArticleId)).ToListAsync();
        translations.Should().HaveCount(Workers);
        translations.Select(t => t.Title).Should().OnlyContain(t => t.EndsWith($"-{Rounds - 1}"));
        (await db.Queryable<Revision>()
                .Where(rv => rv.CollectionName == "article" && itemIds.Contains(rv.ItemId)).CountAsync())
            .Should().Be(Workers * (Rounds + 1));
    }

    [Fact]
    public async Task Concurrent_article_creates_with_tags_all_succeed_and_link_every_tag()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var marker = $"ct-{Guid.NewGuid():N}";
        var tags = new List<string>();
        for (var i = 0; i < 6; i++) tags.Add(await CreateAsync(admin, "tag", new { name = $"{marker}-tag-{i}" }));

        var failures = new ConcurrentBag<string>();
        var created = new ConcurrentBag<Guid>();
        for (var round = 0; round < Rounds; round++)
        {
            var r = round;
            await Task.WhenAll(Enumerable.Range(0, Workers).Select(worker => Task.Run(async () =>
            {
                var body = new
                {
                    status = "draft",
                    tags = new[] { tags[worker % 6], tags[(worker + 1) % 6], tags[(worker + 2) % 6] },
                    translations = new { en = new { title = $"{marker}-{r}-{worker}" } }
                };
                using var response = await admin.PostAsJsonAsync("/api/items/article", body);
                var text = await response.Content.ReadAsStringAsync();
                if (response.StatusCode != HttpStatusCode.Created)
                {
                    failures.Add($"{(int)response.StatusCode}: {text}");
                    return;
                }
                using var doc = JsonDocument.Parse(text);
                created.Add(Guid.Parse(doc.RootElement.GetProperty("data").GetProperty("id").GetString()!));
            })));
        }

        failures.Should().BeEmpty();
        created.Should().HaveCount(Workers * Rounds);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var ids = created.ToList();
        var itemIds = ids.Select(id => id.ToString()).ToList();
        (await db.Queryable<ArticleTag>().Where(l => ids.Contains(l.ArticleId)).CountAsync())
            .Should().Be(ids.Count * 3);
        (await db.Queryable<ArticleTranslation>().Where(t => ids.Contains(t.ArticleId)).CountAsync())
            .Should().Be(ids.Count);
        (await db.Queryable<Revision>()
                .Where(rv => rv.CollectionName == "article" && itemIds.Contains(rv.ItemId)).CountAsync())
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
