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
public class NoPublicFileApiFactory : StruoApiFactory
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

/// <summary>One <see cref="NoPublicFileApiFactory"/> host for a test class, with the public read grant on <c>file</c> removed.</summary>
public sealed class NoPublicFileFixture : IAsyncLifetime
{
    public NoPublicFileApiFactory Factory { get; } = new();

    public Task InitializeAsync() => Factory.RevokePublicFileReadAsync();

    public Task DisposeAsync()
    {
        Factory.Dispose();
        return Task.CompletedTask;
    }
}

// A File, Image or Files field links a row of the file collection: writing one needs read on it, and so does
// a folder id on an upload (it links a mediaFolder row).
// Own host, serialized with the shared one: concurrent host startups of the same entry point are not safe.
[Collection("ApiIntegration")]
public sealed class RelationWriteFileTargetReadTests : IClassFixture<NoPublicFileFixture>
{
    private const string Article = "fxArticle";
    private const string Hero = "heroImageId";
    private const string Gallery = "gallery";
    private const string OgImage = "seoOgImageId";
    private const string FileRefusal = "Read not permitted on 'file'.";

    private readonly NoPublicFileApiFactory factory;
    private readonly RelationWriteHarness h;

    public RelationWriteFileTargetReadTests(NoPublicFileFixture fixture)
    {
        factory = fixture.Factory;
        h = new(factory);
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

        AssertRefusedAlike(outcomes, channel, "file");
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
        outcome.Message.Should().Be(FileRefusal);
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

        AssertWritten(outcome);
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

        AssertWritten(outcome);
    }

    [Fact]
    public async Task Super_admin_writes_a_file_field()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var file = await h.SeedFileAsync();
        var article = await h.SeedArticleAsync(admin);

        var outcome = await h.SendAsync(admin, Rest, Article, article, new() { [Hero] = file.ToString() });

        AssertWritten(outcome);
        (await h.ArticleAsync(article)).HeroImageId.Should().Be(file);
    }

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task Translated_image_write_without_file_read_is_refused_alike_for_every_id(string channel, string op)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var client = await h.CallerAsync([Article], [Article]);
        var file = await h.SeedFileAsync();
        var articlesBefore = await h.ArticleCountAsync();
        var outcomes = new List<WriteOutcome>();
        var seeded = 0;
        foreach (var kind in IdKinds)
        {
            Guid? id = null;
            if (op == Put)
            {
                id = await h.SeedArticleAsync(admin);
                seeded++;
            }
            outcomes.Add(await h.SendAsync(
                client, channel, Article, id, new(), translation: new() { [OgImage] = IdOf(kind, file) }));
        }

        AssertRefusedAlike(outcomes, channel, "file");
        (await h.OgImageLinksToAsync(file)).Should().Be(0);
        (await h.ArticleCountAsync()).Should().Be(articlesBefore + seeded);
    }

    [Fact]
    public async Task Translated_image_key_is_matched_ignoring_case()
    {
        var client = await h.CallerAsync([Article], [Article]);
        var file = await h.SeedFileAsync();
        var article = await h.SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());

        var outcome = await h.SendAsync(
            client, Rest, Article, article, new(), translation: new() { ["SeoOgImageId"] = file.ToString() });

        AssertRefusedAlike([outcome], Rest, "file");
        (await h.OgImageLinksToAsync(file)).Should().Be(0);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Clearing_a_translated_image_without_file_read_is_refused(string channel)
    {
        var client = await h.CallerAsync([Article], [Article]);
        var article = await h.SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());

        var outcome = await h.SendAsync(client, channel, Article, article, new(), translation: new() { [OgImage] = null });

        AssertRefusedAlike([outcome], channel, "file");
    }

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task Translated_image_write_with_file_read_stores_the_link(string channel, string op)
    {
        var client = await h.CallerAsync([Article, "file"], [Article]);
        var file = await h.SeedFileAsync();
        Guid? id = op == Put ? await h.SeedArticleAsync(await factory.CreateAuthenticatedClientAsync()) : null;

        var outcome = await h.SendAsync(client, channel, Article, id, new(), translation: new() { [OgImage] = file.ToString() });

        AssertWritten(outcome);
        (await h.OgImageLinksToAsync(file)).Should().Be(1);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Translated_update_without_an_image_key_needs_no_file_read(string channel)
    {
        var client = await h.CallerAsync([Article], [Article]);
        var article = await h.SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());

        var outcome = await h.SendAsync(client, channel, Article, article, new(), translation: new() { ["title"] = "renamed" });

        AssertWritten(outcome);
    }

    [Fact]
    public async Task Super_admin_writes_a_translated_image()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var file = await h.SeedFileAsync();
        var article = await h.SeedArticleAsync(admin);

        var outcome = await h.SendAsync(admin, Rest, Article, article, new(), translation: new() { [OgImage] = file.ToString() });

        AssertWritten(outcome);
        (await h.OgImageLinksToAsync(file)).Should().Be(1);
    }

    [Fact]
    public async Task Upload_into_a_folder_without_folder_read_is_refused_alike_for_every_id()
    {
        var client = await h.CallerAsync([], ["file"]);
        var folder = await h.SeedFolderAsync();
        var filesBefore = await h.FileCountAsync();
        var outcomes = new List<WriteOutcome>();
        foreach (var kind in IdKinds) outcomes.Add(await h.UploadAsync(client, IdOf(kind, folder)));

        AssertRefusedAlike(outcomes, Rest, "mediaFolder");
        (await h.FileCountAsync()).Should().Be(filesBefore);
        (await h.FilesInFolderAsync(folder)).Should().Be(0);
    }

    [Fact]
    public async Task Upload_into_a_folder_with_folder_read_keeps_its_own_answers()
    {
        var client = await h.CallerAsync(["mediaFolder"], ["file"]);
        var folder = await h.SeedFolderAsync();

        var existing = await h.UploadAsync(client, IdOf(Existing, folder));
        var missing = await h.UploadAsync(client, IdOf(Missing, folder));
        var malformed = await h.UploadAsync(client, IdOf(Malformed, folder));

        AssertWritten(existing);
        (await h.FilesInFolderAsync(folder)).Should().Be(1);
        missing.Message.Should().Be($"Folder '{IdOf(Missing, folder)}' does not exist.");
        malformed.Message.Should().Be("Invalid 'folderId'.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Upload_without_a_folder_id_needs_no_folder_read(string? folderId)
    {
        var client = await h.CallerAsync([], ["file"]);

        AssertWritten(await h.UploadAsync(client, folderId));
    }

    [Fact]
    public async Task Upload_without_file_write_is_refused_on_write_before_the_folder_is_considered()
    {
        var client = await h.CallerAsync([], []);
        var folder = await h.SeedFolderAsync();

        var outcome = await h.UploadAsync(client, folder.ToString());

        outcome.Status.Should().Be(HttpStatusCode.Forbidden);
        outcome.Message.Should().Be("Write not permitted.");
    }

    [Fact]
    public async Task Super_admin_uploads_into_a_folder()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var folder = await h.SeedFolderAsync();

        AssertWritten(await h.UploadAsync(admin, folder.ToString()));
        (await h.FilesInFolderAsync(folder)).Should().BeGreaterThan(0);
    }
}
