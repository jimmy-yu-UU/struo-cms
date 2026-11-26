using System.Net;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// The item read returns the ids of File, Image and Files fields (translated ones too) to every reader of the
// collection, so an update that sends such a field back unchanged needs no read on the file collection. A
// different value, a create, and every other relation key still do.
[Collection("ApiIntegration")]
public sealed class RelationWriteUnchangedFileTests : IClassFixture<NoPublicRelationsFixture>
{
    private const string Article = "fxArticle";
    private const string Hero = "heroImageId";
    private const string Gallery = "gallery";
    private const string OgImage = "seoOgImageId";

    private readonly NoPublicRelationsApiFactory factory;
    private readonly RelationWriteHarness h;

    public RelationWriteUnchangedFileTests(NoPublicRelationsFixture fixture)
    {
        factory = fixture.Factory;
        h = new(factory);
    }

    // An article whose hero image, gallery (two files) and Open Graph image are set.
    private async Task<(Guid Article, Guid First, Guid Second)> SeedLinkedAsync()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (first, second) = (await h.SeedFileAsync(), await h.SeedFileAsync());
        var article = await h.SeedArticleAsync(admin);
        var stored = await h.SendAsync(
            admin, Rest, Article, article,
            new() { [Hero] = first.ToString(), [Gallery] = new[] { first, second } },
            translation: new() { [OgImage] = first.ToString() });
        AssertWritten(stored);
        return (article, first, second);
    }

    private static Dictionary<string, object?> Fields(string field, object? value) => new() { [field] = value };

    private Task<WriteOutcome> SendFieldAsync(
        HttpClient client, string channel, Guid article, string field, object? value, string? status = null)
    {
        var fields = field == OgImage ? new Dictionary<string, object?>() : Fields(field, value);
        if (status is not null) fields["status"] = status;
        return h.SendAsync(
            client, channel, Article, article, fields,
            translation: field == OgImage ? Fields(OgImage, value) : null);
    }

    [Theory]
    [InlineData(Rest, Hero)]
    [InlineData(Rest, Gallery)]
    [InlineData(Rest, OgImage)]
    [InlineData(Gql, Hero)]
    [InlineData(Gql, Gallery)]
    [InlineData(Gql, OgImage)]
    public async Task A_file_field_sent_back_unchanged_is_accepted(string channel, string field)
    {
        var (article, first, second) = await SeedLinkedAsync();
        var client = await h.CallerAsync([Article], [Article]);
        object value = field == Gallery ? new[] { first.ToString(), second.ToString() } : first.ToString();

        var outcome = await SendFieldAsync(client, channel, article, field, value, status: "published");

        AssertWritten(outcome);
        var stored = await h.ArticleAsync(article);
        stored.Status.Should().Be("published");
        stored.HeroImageId.Should().Be(first);
        stored.Gallery.Should().Equal(first, second);
        (await h.OgImageLinksToAsync(first)).Should().Be(1);
    }

    [Theory]
    [InlineData(Rest, Hero)]
    [InlineData(Rest, OgImage)]
    [InlineData(Gql, Hero)]
    [InlineData(Gql, OgImage)]
    public async Task A_cleared_file_field_sent_back_unchanged_is_accepted(string channel, string field)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var article = await h.SeedArticleAsync(admin);
        var client = await h.CallerAsync([Article], [Article]);

        var outcome = await SendFieldAsync(client, channel, article, field, null, status: "published");

        AssertWritten(outcome);
        (await h.ArticleAsync(article)).Status.Should().Be("published");
    }

    [Theory]
    [InlineData(Rest, Hero)]
    [InlineData(Rest, Gallery)]
    [InlineData(Rest, OgImage)]
    [InlineData(Gql, Hero)]
    [InlineData(Gql, Gallery)]
    [InlineData(Gql, OgImage)]
    public async Task A_changed_file_field_is_refused_alike_for_every_id(string channel, string field)
    {
        var (article, first, second) = await SeedLinkedAsync();
        var other = await h.SeedFileAsync();
        var client = await h.CallerAsync([Article], [Article]);
        var outcomes = new List<WriteOutcome>();
        foreach (var kind in IdKinds)
        {
            var id = IdOf(kind, other);
            object value = field == Gallery ? new[] { first.ToString(), id } : id;
            outcomes.Add(await SendFieldAsync(client, channel, article, field, value));
        }

        AssertRefusedAlike(outcomes, channel, "file");
        var stored = await h.ArticleAsync(article);
        stored.HeroImageId.Should().Be(first);
        stored.Gallery.Should().Equal(first, second);
        (await h.OgImageLinksToAsync(first)).Should().Be(1);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_reordered_files_list_is_refused(string channel)
    {
        var (article, first, second) = await SeedLinkedAsync();
        var client = await h.CallerAsync([Article], [Article]);

        var outcome = await SendFieldAsync(client, channel, article, Gallery, new[] { second.ToString(), first.ToString() });

        AssertRefusedAlike([outcome], channel, "file");
        (await h.ArticleAsync(article)).Gallery.Should().Equal(first, second);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task One_changed_file_field_refuses_the_write_although_another_is_unchanged(string channel)
    {
        var (article, first, second) = await SeedLinkedAsync();
        var client = await h.CallerAsync([Article], [Article]);
        var fields = new Dictionary<string, object?> { [Hero] = first.ToString(), [Gallery] = new[] { first.ToString() }, ["status"] = "published" };

        var outcome = await h.SendAsync(client, channel, Article, article, fields);

        AssertRefusedAlike([outcome], channel, "file");
        (await h.ArticleAsync(article)).Status.Should().NotBe("published");
        (await h.ArticleAsync(article)).Gallery.Should().Equal(first, second);
    }

    [Theory]
    [InlineData(Rest, Hero)]
    [InlineData(Rest, Gallery)]
    [InlineData(Gql, Hero)]
    [InlineData(Gql, Gallery)]
    public async Task A_create_with_a_file_field_is_refused_even_when_it_is_empty(string channel, string field)
    {
        var client = await h.CallerAsync([Article], [Article]);
        var value = field == Gallery ? Array.Empty<string>() : (object?)null;
        var before = await h.ArticleCountAsync();

        var outcome = await h.SendAsync(client, channel, Article, null, Fields(field, value), translated: true);

        AssertRefusedAlike([outcome], channel, "file");
        (await h.ArticleCountAsync()).Should().Be(before);
    }

    [Fact]
    public async Task A_missing_item_answers_not_found_before_the_file_field_is_considered()
    {
        var client = await h.CallerAsync([Article], [Article]);

        var outcome = await h.SendAsync(client, Rest, Article, Guid.NewGuid(), Fields(Hero, Guid.NewGuid().ToString()));

        outcome.Status.Should().Be(HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_caller_with_file_read_may_change_the_field(string channel)
    {
        var (article, _, second) = await SeedLinkedAsync();
        var client = await h.CallerAsync([Article, "file"], [Article]);

        AssertWritten(await SendFieldAsync(client, channel, article, Hero, second.ToString()));

        (await h.ArticleAsync(article)).HeroImageId.Should().Be(second);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_many_to_many_array_sent_back_unchanged_is_still_refused(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var tag = await h.SeedTagAsync();
        var article = await h.SeedArticleAsync(admin, tag);
        var client = await h.CallerAsync([Article], [Article]);

        var outcome = await h.SendAsync(client, channel, Article, article, new() { ["tags"] = new[] { tag.ToString() } });

        AssertRefusedAlike([outcome], channel, "fxTag");
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_many_to_one_key_sent_back_unchanged_is_still_refused(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var category = await h.SeedCategoryAsync();
        var article = await h.SeedArticleAsync(admin);
        AssertWritten(await h.SendAsync(admin, Rest, Article, article, new() { ["categoryId"] = category.ToString() }));
        var client = await h.CallerAsync([Article], [Article]);

        var outcome = await h.SendAsync(client, channel, Article, article, new() { ["categoryId"] = category.ToString() });

        AssertRefusedAlike([outcome], channel, "fxCategory");
    }

    [Fact]
    public async Task The_item_read_returns_the_file_ids_and_leaves_out_the_keys_of_unreadable_relations()
    {
        var (article, first, second) = await SeedLinkedAsync();
        var admin = await factory.CreateAuthenticatedClientAsync();
        var category = await h.SeedCategoryAsync();
        AssertWritten(await h.SendAsync(admin, Rest, Article, article, new() { ["categoryId"] = category.ToString() }));
        var client = await h.CallerAsync([Article], [Article]);

        var read = await h.ReadItemAsync(client, Article, article);

        read.GetProperty(Hero).GetGuid().Should().Be(first);
        read.GetProperty(Gallery).EnumerateArray().Select(e => e.GetGuid()).Should().Equal(first, second);
        read.GetProperty("translations").GetProperty("en").GetProperty(OgImage).GetGuid().Should().Be(first);
        read.TryGetProperty("categoryId", out _).Should().BeFalse();
        read.TryGetProperty("tags", out _).Should().BeFalse();
    }
}
