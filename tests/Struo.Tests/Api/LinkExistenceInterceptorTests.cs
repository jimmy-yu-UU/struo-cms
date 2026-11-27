using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Query;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// The existence check reads the links the request body sent, before any interceptor runs: an id that server
// code assigns in BeforeWriteAsync is not checked, on a create and on an update alike.
[Collection("ApiIntegration")]
public sealed class LinkExistenceInterceptorTests(ApiFactory factory)
{
    private static readonly Guid Dangling = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly object Translations = new { en = new { title = "t" } };

    private sealed class AssignsDanglingCategory : IItemWriteInterceptor
    {
        public bool AppliesTo(string collection) => collection == "fxArticle";

        public Task BeforeWriteAsync(ItemWriteContext context, CancellationToken ct)
        {
            if (context.Operation is ItemWriteOperation.Create or ItemWriteOperation.Update
                && context.Entity is FxArticle article)
                article.CategoryId = Dangling;
            return Task.CompletedTask;
        }
    }

    private async Task<(Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program> Host, HttpClient Client)> HostAsync()
    {
        var host = factory.WithWebHostBuilder(
            b => b.ConfigureServices(s => s.AddScoped<IItemWriteInterceptor, AssignsDanglingCategory>()));
        var client = host.CreateClient();
        await factory.EnsureAdminSeededAsync();
        client.DefaultRequestHeaders.Add("X-Struo-CSRF", "1");
        (await client.PostAsJsonAsync("/api/auth/login",
            new { email = ApiFactory.AdminEmail, password = ApiFactory.AdminPassword })).EnsureSuccessStatusCode();
        return (host, client);
    }

    private async Task<Guid> SeedCategoryAsync() => await new RelationWriteHarness(factory).SeedCategoryAsync();

    [Fact]
    public async Task An_id_an_interceptor_assigns_on_a_create_is_not_checked()
    {
        var category = await SeedCategoryAsync();
        var (host, client) = await HostAsync();
        using var _ = host;

        var resp = await client.PostAsJsonAsync("/api/items/fxArticle", new { categoryId = category, translations = Translations });

        resp.EnsureSuccessStatusCode();
        var created = (await resp.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data");
        var id = Guid.Parse(created.GetProperty("id").GetString()!);
        (await new RelationWriteHarness(factory).ArticleAsync(id)).CategoryId.Should().Be(Dangling);
    }

    [Fact]
    public async Task An_id_an_interceptor_assigns_on_an_update_is_not_checked()
    {
        var category = await SeedCategoryAsync();
        var (host, client) = await HostAsync();
        using var _ = host;
        var created = await client.PostAsJsonAsync("/api/items/fxArticle", new { translations = Translations });
        created.EnsureSuccessStatusCode();
        var id = Guid.Parse((await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("data")
            .GetProperty("id").GetString()!);

        var resp = await client.PutAsJsonAsync($"/api/items/fxArticle/{id}", new { categoryId = category });

        resp.EnsureSuccessStatusCode();
        (await new RelationWriteHarness(factory).ArticleAsync(id)).CategoryId.Should().Be(Dangling);
    }
}
