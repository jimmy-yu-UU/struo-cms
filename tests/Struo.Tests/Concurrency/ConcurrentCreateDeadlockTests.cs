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
/// Concurrent creates of a collection with a translation sidecar and revisions, through the REST
/// stack. Every create must succeed and leave exactly one translation row and one revision row. On a
/// live backend the writes run in parallel transactions; SQLite serializes writers.
/// </summary>
[Collection("ApiIntegration")]
public class ConcurrentCreateDeadlockTests(ApiFactory factory)
{
    private const int Workers = 16;
    private const int Rounds = 5;

    [Fact]
    public async Task Concurrent_article_creates_all_succeed()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var marker = $"cc-{Guid.NewGuid():N}";
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
                created.Add(Guid.Parse(doc.RootElement.GetProperty("data").GetProperty("id").GetString()!));
            })));
        }

        failures.Should().BeEmpty();
        created.Should().HaveCount(Workers * Rounds);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var ids = created.ToList();
        var itemIds = ids.Select(id => id.ToString()).ToList();
        (await db.Queryable<FxArticleTranslation>().Where(t => ids.Contains(t.FxArticleId)).CountAsync())
            .Should().Be(ids.Count);
        (await db.Queryable<Revision>()
                .Where(rv => rv.CollectionName == "fxArticle" && itemIds.Contains(rv.ItemId)).CountAsync())
            .Should().Be(ids.Count);
    }
}
