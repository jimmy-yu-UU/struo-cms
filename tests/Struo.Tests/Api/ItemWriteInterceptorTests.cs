// tests/Struo.Tests/Api/ItemWriteInterceptorTests.cs
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Query;
using Struo.Domain;
using Struo.Infrastructure.Revisions;
using Struo.Sample.Blog;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// Drives IItemWriteInterceptor through the real host: every hook of every operation, DI registration
/// order, entity mutation, the transaction boundary and the error mapping on REST and GraphQL.
/// </summary>
[Collection("ApiIntegration")]
public class ItemWriteInterceptorTests(ApiFactory factory)
{
    private sealed class RuleViolationException(string message)
        : StruoDomainException("ACME_RULE_VIOLATION", 429, message);

    private sealed record HookCall(string Interceptor, string Hook, ItemWriteOperation Operation, string? Id, string[] BodyKeys);

    private sealed class Script
    {
        public readonly List<HookCall> Calls = [];
        public string? ThrowIn;
        public bool MutateStatus;
    }

    private sealed class ScriptedInterceptor(string name, string collection, Script script) : IItemWriteInterceptor
    {
        public bool AppliesTo(string c) => c == collection;

        public Task BeforeWriteAsync(ItemWriteContext context, CancellationToken ct)
        {
            if (script.MutateStatus && context.Entity is Article article) article.Status = "published";
            return Step(context, "BeforeWrite");
        }

        public Task InTransactionAsync(ItemWriteContext context, CancellationToken ct) => Step(context, "InTransaction");
        public Task AfterCommitAsync(ItemWriteContext context, CancellationToken ct) => Step(context, "AfterCommit");
        public Task AfterNotifyAsync(ItemWriteContext context, CancellationToken ct) => Step(context, "AfterNotify");

        private Task Step(ItemWriteContext context, string hook)
        {
            lock (script.Calls)
                script.Calls.Add(new HookCall(name, hook, context.Operation, context.Id, context.BodyKeys.Order().ToArray()));
            if (script.ThrowIn == hook)
                throw new RuleViolationException($"{hook} refused.");
            return Task.CompletedTask;
        }
    }

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    private async Task<(WebApplicationFactory<Program> Host, HttpClient Client, Script Script)> HostAsync(
        string collection = "article", params string[] interceptorNames)
    {
        var script = new Script();
        var names = interceptorNames.Length == 0 ? new[] { "A" } : interceptorNames;
        var host = factory.WithWebHostBuilder(h => h.ConfigureServices(s =>
        {
            foreach (var name in names)
                s.AddScoped<IItemWriteInterceptor>(_ => new ScriptedInterceptor(name, collection, script));
        }));
        var client = host.CreateClient();
        await factory.EnsureAdminSeededAsync();
        client.DefaultRequestHeaders.Add("X-Struo-CSRF", "1");
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword });
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        return (host, client, script);
    }

    private static async Task<string> CreateArticleAsync(HttpClient client, object? body = null)
    {
        var response = await client.PostAsJsonAsync("/api/items/article",
            body ?? new { status = "draft", translations = new { en = new { title = "t-" + Guid.NewGuid().ToString("N")[..6] } } });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return Root(await response.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    private static string[] Sequence(Script script) =>
        script.Calls.Select(c => $"{c.Interceptor}:{c.Hook}:{c.Operation}").ToArray();

    private static string[] AllHooks(ItemWriteOperation operation) =>
    [
        $"A:BeforeWrite:{operation}", $"B:BeforeWrite:{operation}",
        $"A:InTransaction:{operation}", $"B:InTransaction:{operation}",
        $"A:AfterCommit:{operation}", $"B:AfterCommit:{operation}",
        $"A:AfterNotify:{operation}", $"B:AfterNotify:{operation}",
    ];

    [Fact]
    public async Task Every_operation_runs_every_hook_for_every_interceptor_in_registration_order()
    {
        var (_, client, script) = await HostAsync("article", "A", "B");

        var id = await CreateArticleAsync(client);
        Sequence(script).Should().Equal(AllHooks(ItemWriteOperation.Create));
        script.Calls[0].Id.Should().BeNull();
        script.Calls.Should().OnlyContain(c => c.BodyKeys.SequenceEqual(new[] { "status", "translations" }));
        script.Calls.Skip(2).Should().OnlyContain(c => c.Id == id);

        script.Calls.Clear();
        (await client.PutAsJsonAsync($"/api/items/article/{id}", new { status = "published" }))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        Sequence(script).Should().Equal(AllHooks(ItemWriteOperation.Update));
        script.Calls.Should().OnlyContain(c => c.Id == id && c.BodyKeys.SequenceEqual(new[] { "status" }));

        script.Calls.Clear();
        (await client.PostAsync($"/api/items/article/{id}/revisions/1/revert", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
        Sequence(script).Should().Equal(AllHooks(ItemWriteOperation.Revert));
        script.Calls.Should().OnlyContain(c => c.Id == id && c.BodyKeys.Contains("status"));

        script.Calls.Clear();
        (await client.DeleteAsync($"/api/items/article/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        Sequence(script).Should().Equal(AllHooks(ItemWriteOperation.Trash));
        script.Calls.Should().OnlyContain(c => c.Id == id && c.BodyKeys.Length == 0);

        script.Calls.Clear();
        (await client.PostAsync($"/api/items/article/{id}/restore", null)).StatusCode.Should().Be(HttpStatusCode.OK);
        Sequence(script).Should().Equal(AllHooks(ItemWriteOperation.Restore));
        script.Calls.Should().OnlyContain(c => c.Id == id && c.BodyKeys.Length == 0);

        script.Calls.Clear();
        (await client.DeleteAsync($"/api/items/article/{id}?purge=true")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        Sequence(script).Should().Equal(AllHooks(ItemWriteOperation.Purge));
        script.Calls.Should().OnlyContain(c => c.Id == id && c.BodyKeys.Length == 0);
    }

    [Fact]
    public async Task A_second_trash_of_a_trashed_row_runs_only_BeforeWrite_and_AfterNotify()
    {
        var (_, client, script) = await HostAsync();
        var id = await CreateArticleAsync(client);
        (await client.DeleteAsync($"/api/items/article/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        script.Calls.Clear();
        (await client.DeleteAsync($"/api/items/article/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        Sequence(script).Should().Equal("A:BeforeWrite:Trash", "A:AfterNotify:Trash");
    }

    [Fact]
    public async Task An_interceptor_that_does_not_apply_to_the_collection_is_never_called()
    {
        var (_, client, script) = await HostAsync("category");

        await CreateArticleAsync(client);

        script.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_BeforeWrite_mutation_of_the_entity_is_persisted()
    {
        var (_, client, script) = await HostAsync();
        script.MutateStatus = true;

        var id = await CreateArticleAsync(client, new { status = "draft", translations = new { en = new { title = "m" } } });

        var get = await client.GetAsync($"/api/items/article/{id}");
        Root(await get.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("status").GetString()
            .Should().Be("published");
    }

    private sealed record Counts(int Articles, int Junctions, int Translations, int Revisions);

    private static async Task<Counts> CountsAsync(WebApplicationFactory<Program> host)
    {
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        return new Counts(
            await db.Queryable<Article>().CountAsync(),
            await db.Queryable<ArticleTag>().CountAsync(),
            await db.Queryable<ArticleTranslation>().CountAsync(),
            await db.Queryable<Revision>().Where(r => r.CollectionName == "article").CountAsync());
    }

    private static async Task<string> CreateTagAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/items/tag", new { name = "tag-" + Guid.NewGuid().ToString("N")[..8] });
        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return Root(await response.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    [Fact]
    public async Task An_InTransaction_throw_rolls_back_the_row_junctions_translations_and_revision_on_create()
    {
        var (host, client, script) = await HostAsync();
        var tag = await CreateTagAsync(client);
        var before = await CountsAsync(host);
        script.ThrowIn = "InTransaction";

        var response = await client.PostAsJsonAsync("/api/items/article",
            new { status = "draft", tags = new[] { tag }, translations = new { en = new { title = "rolled back" } } });

        response.StatusCode.Should().Be((HttpStatusCode)429, await response.Content.ReadAsStringAsync());
        (await CountsAsync(host)).Should().Be(before);
    }

    [Fact]
    public async Task An_InTransaction_throw_rolls_back_an_update()
    {
        var (host, client, script) = await HostAsync();
        var tag = await CreateTagAsync(client);
        var id = await CreateArticleAsync(client, new
        {
            status = "draft", tags = new[] { tag }, translations = new { en = new { title = "keep" } },
        });
        var before = await CountsAsync(host);
        script.ThrowIn = "InTransaction";

        var response = await client.PutAsJsonAsync($"/api/items/article/{id}",
            new { status = "published", tags = Array.Empty<string>(), translations = new { en = new { title = "changed" } } });

        response.StatusCode.Should().Be((HttpStatusCode)429, await response.Content.ReadAsStringAsync());
        (await CountsAsync(host)).Should().Be(before);
        var get = await client.GetAsync($"/api/items/article/{id}");
        var data = Root(await get.Content.ReadAsStringAsync()).GetProperty("data");
        data.GetProperty("status").GetString().Should().Be("draft");
        data.GetProperty("translations").GetProperty("en").GetProperty("title").GetString().Should().Be("keep");
    }

    [Fact]
    public async Task A_StruoDomainException_from_BeforeWrite_surfaces_as_its_own_status_on_REST()
    {
        var (host, client, script) = await HostAsync();
        script.ThrowIn = "BeforeWrite";
        var before = await CountsAsync(host);

        var response = await client.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "refused" } } });
        var json = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be((HttpStatusCode)429, json);
        Root(json).GetProperty("error").GetProperty("code").GetString().Should().Be("ACME_RULE_VIOLATION");
        (await CountsAsync(host)).Should().Be(before);
    }

    [Fact]
    public async Task A_StruoDomainException_from_BeforeWrite_surfaces_with_its_code_on_GraphQL()
    {
        var (_, client, script) = await HostAsync("category");
        script.ThrowIn = "BeforeWrite";

        var response = await client.PostAsJsonAsync("/graphql",
            new { query = "mutation { createCategory(input: { name: \"gql-refused\" }) { id } }" });
        var json = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK, json);
        var error = Root(json).GetProperty("errors")[0];
        error.GetProperty("extensions").GetProperty("code").GetString().Should().Be("ACME_RULE_VIOLATION");
        error.GetProperty("message").GetString().Should().Be("BeforeWrite refused.");
    }

    [Fact]
    public async Task An_AfterCommit_throw_propagates_but_the_row_stays_committed()
    {
        var (host, client, script) = await HostAsync();
        var before = await CountsAsync(host);
        script.ThrowIn = "AfterCommit";

        var response = await client.PostAsJsonAsync("/api/items/article",
            new { status = "draft", translations = new { en = new { title = "committed" } } });

        response.StatusCode.Should().Be((HttpStatusCode)429, await response.Content.ReadAsStringAsync());
        var after = await CountsAsync(host);
        after.Articles.Should().Be(before.Articles + 1);
        after.Revisions.Should().Be(before.Revisions + 1);
    }

    [Fact]
    public async Task A_cascaded_purge_does_not_invoke_the_interceptor_of_the_affected_children()
    {
        var (host, client, script) = await HostAsync();
        var category = await client.PostAsJsonAsync("/api/items/category", new { name = "cascade-" + Guid.NewGuid().ToString("N")[..6] });
        var categoryId = Root(await category.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
        var articleId = await CreateArticleAsync(client, new
        {
            status = "draft", categoryId, translations = new { en = new { title = "child" } },
        });
        script.Calls.Clear();

        (await client.DeleteAsync($"/api/items/category/{categoryId}?purge=true")).StatusCode
            .Should().Be(HttpStatusCode.NoContent);

        script.Calls.Should().BeEmpty();
        using var scope = host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var child = await db.Queryable<Article>().Where(a => a.Id == Guid.Parse(articleId)).FirstAsync();
        child.CategoryId.Should().BeNull("the purge set the child's foreign key to null");
    }

    private sealed class FillNameInterceptor : IItemWriteInterceptor
    {
        public bool AppliesTo(string collection) => collection == "category";

        public Task BeforeWriteAsync(ItemWriteContext context, CancellationToken ct)
        {
            if (context.Operation == ItemWriteOperation.Create && context.Entity is Category category)
                category.Name = "generated";
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task BeforeWrite_runs_before_the_Required_check_so_an_interceptor_can_fill_a_required_field()
    {
        var client = factory.WithWebHostBuilder(h => h.ConfigureServices(s =>
            s.AddScoped<IItemWriteInterceptor, FillNameInterceptor>())).CreateClient();
        await factory.EnsureAdminSeededAsync();
        client.DefaultRequestHeaders.Add("X-Struo-CSRF", "1");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync("/api/items/category", new { });

        response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        Root(await response.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("name").GetString()
            .Should().Be("generated");
    }

    private sealed class RecordingDeleteHandler(List<string> calls) : ICollectionDeleteHandler
    {
        public bool AppliesTo(string collection) => collection == "category";

        public Task<bool> TrashAsync(string id, CancellationToken ct)
        {
            calls.Add("trash:" + id);
            return Task.FromResult(true);
        }

        public Task<bool> PurgeAsync(string id, CancellationToken ct)
        {
            calls.Add("purge:" + id);
            return Task.FromResult(true);
        }
    }

    [Fact]
    public async Task A_delete_handler_replaces_trash_and_purge_after_BeforeWrite()
    {
        var script = new Script();
        var calls = new List<string>();
        var client = factory.WithWebHostBuilder(h => h.ConfigureServices(s =>
        {
            s.AddScoped<IItemWriteInterceptor>(_ => new ScriptedInterceptor("A", "category", script));
            s.AddScoped<ICollectionDeleteHandler>(_ => new RecordingDeleteHandler(calls));
        })).CreateClient();
        await factory.EnsureAdminSeededAsync();
        client.DefaultRequestHeaders.Add("X-Struo-CSRF", "1");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();
        var created = await client.PostAsJsonAsync("/api/items/category", new { name = "handled-" + Guid.NewGuid().ToString("N")[..6] });
        var id = Root(await created.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
        script.Calls.Clear();

        (await client.DeleteAsync($"/api/items/category/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);
        (await client.DeleteAsync($"/api/items/category/{id}?purge=true")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        calls.Should().Equal($"trash:{id}", $"purge:{id}");
        Sequence(script).Should().Equal("A:BeforeWrite:Trash", "A:BeforeWrite:Purge");
        var get = await client.GetAsync($"/api/items/category/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK, "the handler never touched the row");
    }
}
