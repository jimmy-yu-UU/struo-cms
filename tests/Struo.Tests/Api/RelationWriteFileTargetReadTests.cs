using System.Net;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Infrastructure.Identity;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

/// <summary>A host where the <c>file</c> collection is not publicly readable.</summary>
public sealed class NoPublicFileApiFactory : StruoApiFactory
{
    protected override IReadOnlyList<string> PublicReadCollections => ["fxArticle", "fxCategory"];

    /// <summary>Removes the public role's read grant on <c>file</c>, which every host seeds.</summary>
    public async Task RevokePublicFileReadAsync()
    {
        await EnsureAdminSeededAsync();
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var publicRole = await db.Queryable<Role>().Where(r => r.Name == "public").FirstAsync();
        await db.Deleteable<Permission>().Where(p => p.RoleId == publicRole.Id && p.Collection == "file").ExecuteCommandAsync();
    }
}

// A File, Image or Files field links a row of the file collection: writing one needs read on it.
// Own host, serialized with the shared one: concurrent host startups of the same entry point are not safe.
[Collection("ApiIntegration")]
public sealed class RelationWriteFileTargetReadTests : IAsyncLifetime
{
    private const string Article = "fxArticle";
    private const string Hero = "heroImageId";
    private const string Gallery = "gallery";

    private readonly NoPublicFileApiFactory factory = new();
    private readonly RelationWriteHarness h;

    public RelationWriteFileTargetReadTests() => h = new(factory);

    public Task InitializeAsync() => factory.RevokePublicFileReadAsync();

    public Task DisposeAsync()
    {
        factory.Dispose();
        return Task.CompletedTask;
    }

    private static object Value(string field, string id) => field == Gallery ? new[] { id } : id;

    [Fact]
    public async Task The_host_does_not_let_an_article_editor_read_files()
    {
        var client = await h.CallerAsync([Article], [Article]);
        var file = await h.SeedFileAsync();

        (await client.GetAsync($"/api/items/file/{file}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Theory]
    [InlineData(Rest, Post, Hero)]
    [InlineData(Rest, Put, Hero)]
    [InlineData(Rest, Post, Gallery)]
    [InlineData(Rest, Put, Gallery)]
    [InlineData(Gql, Post, Hero)]
    [InlineData(Gql, Put, Hero)]
    [InlineData(Gql, Post, Gallery)]
    [InlineData(Gql, Put, Gallery)]
    public async Task File_field_write_without_file_read_is_refused_alike_for_every_id(string channel, string op, string field)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var client = await h.CallerAsync([Article], [Article]);
        var file = await h.SeedFileAsync();
        var articlesBefore = await h.ArticleCountAsync();
        var outcomes = new List<WriteOutcome>();
        var seeded = new List<Guid>();
        foreach (var kind in IdKinds)
        {
            Guid? id = null;
            if (op == Put)
            {
                id = await h.SeedArticleAsync(admin);
                seeded.Add(id.Value);
            }
            outcomes.Add(await h.SendAsync(
                client, channel, Article, id, new() { [field] = Value(field, IdOf(kind, file)) }, translated: true));
        }

        foreach (var outcome in outcomes)
        {
            outcome.Code.Should().Be("FORBIDDEN");
            outcome.Message.Should().Be("Read not permitted on 'file'.");
            if (channel == Rest) outcome.Status.Should().Be(HttpStatusCode.Forbidden);
        }
        outcomes.Select(o => o.Body).Distinct().Should().ContainSingle();
        (await h.ArticleCountAsync()).Should().Be(articlesBefore + seeded.Count);
        foreach (var id in seeded)
        {
            var stored = await h.ArticleAsync(id);
            stored.HeroImageId.Should().BeNull();
            stored.Gallery.Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Clearing_a_file_field_without_file_read_is_refused(string channel)
    {
        var client = await h.CallerAsync([Article], [Article]);
        var article = await h.SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());

        var outcome = await h.SendAsync(client, channel, Article, article, new() { [Hero] = null });

        outcome.Code.Should().Be("FORBIDDEN");
        outcome.Message.Should().Be("Read not permitted on 'file'.");
    }

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task File_field_write_with_file_read_stores_the_link(string channel, string op)
    {
        var client = await h.CallerAsync([Article, "file"], [Article]);
        var file = await h.SeedFileAsync();
        Guid? id = op == Put ? await h.SeedArticleAsync(await factory.CreateAuthenticatedClientAsync()) : null;

        var outcome = await h.SendAsync(client, channel, Article, id, new() { [Hero] = file.ToString() }, translated: true);

        outcome.Code.Should().BeNull(outcome.Body);
        if (id is { } stored) (await h.ArticleAsync(stored)).HeroImageId.Should().Be(file);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Update_without_a_file_key_needs_no_file_read(string channel)
    {
        var client = await h.CallerAsync([Article], [Article]);
        var article = await h.SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());

        var outcome = await h.SendAsync(client, channel, Article, article, new() { ["status"] = "published" });

        outcome.Code.Should().BeNull(outcome.Body);
    }

    [Fact]
    public async Task Super_admin_writes_a_file_field()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var file = await h.SeedFileAsync();
        var article = await h.SeedArticleAsync(admin);

        var outcome = await h.SendAsync(admin, Rest, Article, article, new() { [Hero] = file.ToString() });

        outcome.Code.Should().BeNull(outcome.Body);
        (await h.ArticleAsync(article)).HeroImageId.Should().Be(file);
    }
}
