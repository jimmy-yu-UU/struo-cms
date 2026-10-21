using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Query;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;
using FileRow = Struo.Infrastructure.Files.File;

namespace Struo.Tests.Api;

/// <summary>
/// One process-wide SqlSugarScope serves every request, so each logical flow must still get its own
/// connection, transaction and audit user. These run against the live backends too: a SQLite file
/// does not reproduce the shared-connection race, PostgreSQL does.
/// </summary>
[Collection("ApiIntegration")]
public class SqlSugarSingletonScopeApiTests(ApiFactory factory)
{
    private const string FourRootQuery =
        "{ articles { items { id } } categories { items { id } } tags { items { id } } files { items { id } } }";

    [Fact]
    public async Task Concurrent_multi_root_queries_each_return_the_complete_data()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var tags = new List<string>();
        for (var i = 0; i < 3; i++) tags.Add(await CreateAsync(admin, "tag", new { name = $"gq-{Guid.NewGuid():N}" }));
        var categories = new List<string>();
        for (var i = 0; i < 2; i++) categories.Add(await CreateAsync(admin, "category", new { name = $"gc-{Guid.NewGuid():N}" }));
        var files = new List<string>();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            for (var i = 0; i < 2; i++)
            {
                var file = new FileRow
                {
                    Id = Guid.CreateVersion7(), StorageKey = $"k-{Guid.NewGuid():N}", FileName = "f.png",
                    ContentType = "image/png", Size = 1, Status = "published"
                };
                await db.Insertable(file).ExecuteCommandAsync();
                files.Add(file.Id.ToString());
            }
        }

        var failures = new ConcurrentBag<string>();
        for (var round = 0; round < 25; round++)
        {
            await Task.WhenAll(Enumerable.Range(0, 8).Select(async worker =>
            {
                using var response = await admin.PostAsJsonAsync("/graphql", new { query = FourRootQuery });
                var body = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(body);
                if (response.StatusCode != HttpStatusCode.OK || doc.RootElement.TryGetProperty("errors", out _))
                {
                    failures.Add(body);
                    return;
                }
                var data = doc.RootElement.GetProperty("data");
                if (!Contains(data, "tags", tags) || !Contains(data, "categories", categories) || !Contains(data, "files", files))
                    failures.Add("incomplete: " + body);
            }));
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public async Task Concurrent_rollbacks_stay_isolated_from_concurrent_writers()
    {
        using var admin = await factory.CreateAuthenticatedClientAsync();
        var marker = $"rb-{Guid.NewGuid():N}";
        var seenInside = new ConcurrentBag<int>();
        var unexpected = new ConcurrentBag<string>();
        var acknowledged = new ConcurrentBag<Guid>();
        const int rounds = 15;

        for (var round = 0; round < rounds; round++)
        {
            var work = new List<Task>();
            for (var k = 0; k < 3; k++)
            {
                var name = $"{marker}-{round}-{k}";
                work.Add(Task.Run(() => RollBackTagInsertAsync(name, seenInside, unexpected)));
                var okName = $"ok-{marker}-{round}-{k}";
                work.Add(Task.Run(async () =>
                {
                    // A REST write that fails is unexpected, including a deadlock victim: the API masks it
                    // as a 500, and plain tag inserts do not deadlock.
                    try { acknowledged.Add(Guid.Parse(await CreateAsync(admin, "tag", new { name = okName }))); }
                    catch (Exception ex) { unexpected.Add(ex.Message); }
                }));
            }
            await Task.WhenAll(work);
        }

        int leaked, present;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            leaked = await db.Queryable<Tag>().Where(t => t.Name.StartsWith(marker)).CountAsync();
            var ids = acknowledged.ToList();
            present = await db.Queryable<Tag>().Where(t => ids.Contains(t.Id)).CountAsync();
        }

        unexpected.Should().BeEmpty();
        // Floors: a run where every rollback flow deadlocked would otherwise pass on empty bags.
        seenInside.Should().NotBeEmpty();
        acknowledged.Should().NotBeEmpty();
        seenInside.Should().OnlyContain(count => count == 1);
        leaked.Should().Be(0);
        present.Should().Be(acknowledged.Count);
    }

    [Fact]
    public async Task Concurrent_writes_by_different_users_are_stamped_with_their_own_id()
    {
        var users = new List<(HttpClient Client, Guid Id)>();
        using var admin = await factory.CreateAuthenticatedClientAsync();
        users.Add((admin, factory.AdminUserId));
        for (var i = 0; i < 3; i++)
            users.Add(await factory.CreateEditorClientAsync(["tag"], ["tag"]));
        var created = new ConcurrentBag<(Guid TagId, Guid User)>();

        await Task.WhenAll(users.Select(user => Task.Run(async () =>
        {
            for (var i = 0; i < 25; i++)
            {
                var id = await CreateAsync(user.Client, "tag", new { name = $"au-{user.Id:N}-{i}" });
                created.Add((Guid.Parse(id), user.Id));
            }
        })));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var ids = created.Select(c => c.TagId).ToList();
        var stamps = (await db.Queryable<Tag>().Where(t => ids.Contains(t.Id)).ToListAsync())
            .ToDictionary(t => t.Id);
        created.Should().HaveCount(users.Count * 25);
        foreach (var (tagId, user) in created)
        {
            stamps[tagId].CreatedBy.Should().Be(user);
            stamps[tagId].UpdatedBy.Should().Be(user);
        }
    }

    private async Task RollBackTagInsertAsync(string name, ConcurrentBag<int> seenInside, ConcurrentBag<string> unexpected)
    {
        using var scope = factory.Services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IItemRepository>();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        try
        {
            await repo.InTransactionAsync(async () =>
            {
                await db.Insertable(new Tag { Id = Guid.CreateVersion7(), Name = name }).ExecuteCommandAsync();
                seenInside.Add(await db.Queryable<Tag>().Where(t => t.Name == name).CountAsync());
                await Task.Delay(50);
                throw new RollbackRequested();
            });
        }
        catch (RollbackRequested) { }
        catch (Exception ex) when (IsDeadlock(ex)) { }
        catch (Exception ex) { unexpected.Add(ex.Message); }
    }

    private sealed class RollbackRequested : Exception;

    // A direct database call in a rollback flow can be chosen as a deadlock victim on SQL Server,
    // MySQL and MariaDB; that is database behaviour and not what this test looks for. Nothing else
    // is tolerated.
    private static bool IsDeadlock(Exception ex) =>
        ex.ToString().Contains("deadlock", StringComparison.OrdinalIgnoreCase);

    private static bool Contains(JsonElement data, string root, IEnumerable<string> ids)
    {
        var present = data.GetProperty(root).GetProperty("items").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()).ToHashSet();
        return ids.All(present.Contains);
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
