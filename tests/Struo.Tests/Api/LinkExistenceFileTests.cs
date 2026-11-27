using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// A File, Image or Files field (translated ones too) must name existing files when it is newly assigned: an id
// equal to the stored one is not checked, a trashed file is not a valid new assignment, and null clears. A
// revert may assign rows trashed since its snapshot.
[Collection("ApiIntegration")]
public class LinkExistenceFileTests(ApiFactory factory)
{
    private const string Article = "fxArticle";
    private const string Hero = "heroImageId";
    private const string Gallery = "gallery";
    private const string OgImage = "seoOgImageId";
    private const string SingleMissing = "The id in '{0}' does not exist in 'file'.";
    private const string ListMissing = "One or more ids in 'gallery' do not exist in 'file'.";

    private readonly RelationWriteHarness h = new(factory);

    private static string MissingId => IdOf(Missing, Guid.Empty);

    private static void AssertRefused(WriteOutcome outcome, string message)
    {
        outcome.Code.Should().Be("BAD_USER_INPUT", outcome.Body);
        outcome.Message.Should().Be(message);
    }

    private static string MessageOf(string field) => field == Gallery ? ListMissing : string.Format(SingleMissing, field);

    private static object Value(string field, string id) => field == Gallery ? new[] { id } : id;

    private static Task<WriteOutcome> SendFieldAsync(
        HttpClient client, string channel, Guid? article, string field, object? value, string? status = null)
    {
        var fields = field == OgImage ? new Dictionary<string, object?>() : new Dictionary<string, object?> { [field] = value };
        if (status is not null) fields["status"] = status;
        return SendAsync(
            client, channel, Article, article, fields, translated: article is null,
            translation: field == OgImage ? new() { [OgImage] = value } : null);
    }

    [Theory]
    [InlineData(Rest, Post, Hero)]
    [InlineData(Rest, Put, Hero)]
    [InlineData(Rest, Post, Gallery)]
    [InlineData(Rest, Put, Gallery)]
    [InlineData(Rest, Post, OgImage)]
    [InlineData(Rest, Put, OgImage)]
    [InlineData(Gql, Post, Hero)]
    [InlineData(Gql, Put, Hero)]
    [InlineData(Gql, Post, Gallery)]
    [InlineData(Gql, Put, Gallery)]
    [InlineData(Gql, Post, OgImage)]
    [InlineData(Gql, Put, OgImage)]
    public async Task A_file_field_naming_a_missing_file_is_refused_and_nothing_is_stored(string channel, string op, string field)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        Guid? article = op == Put ? await SeedArticleAsync(admin) : null;
        var before = await h.ArticleCountAsync();

        var outcome = await SendFieldAsync(admin, channel, article, field, Value(field, MissingId));

        AssertRefused(outcome, MessageOf(field));
        if (article is { } id) (await h.StoredLinksAsync(id)).Should().BeEquivalentTo(new ArticleLinks("draft", "seed", null, [], null, [], null));
        else (await h.ArticleCountAsync()).Should().Be(before);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_files_list_with_one_missing_file_is_refused_as_a_whole(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var file = await h.SeedFileAsync();
        var article = await SeedArticleAsync(admin);

        var outcome = await SendAsync(
            admin, channel, Article, article, new() { [Gallery] = new[] { file.ToString(), MissingId } });

        AssertRefused(outcome, ListMissing);
        (await h.ArticleAsync(article)).Gallery.Should().BeEmpty();
    }

    [Theory]
    [InlineData(Rest, Post)]
    [InlineData(Rest, Put)]
    [InlineData(Gql, Post)]
    [InlineData(Gql, Put)]
    public async Task File_fields_naming_existing_files_are_stored(string channel, string op)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var file = await h.SeedFileAsync();
        Guid? article = op == Put ? await SeedArticleAsync(admin) : null;
        var fields = new Dictionary<string, object?> { [Hero] = file.ToString(), [Gallery] = new[] { file.ToString() } };

        var outcome = await SendAsync(
            admin, channel, Article, article, fields, translated: true, translation: new() { [OgImage] = file.ToString() });

        AssertWritten(outcome);
        (await h.OgImageLinksToAsync(file)).Should().Be(1);
        if (article is { } id) (await h.ArticleAsync(id)).Gallery.Should().Equal(file);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Dangling_file_fields_sent_back_unchanged_are_accepted_when_another_field_changes(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var missing = Guid.Parse(MissingId);
        var article = await h.SeedDanglingArticleAsync(missing);

        var outcome = await SendAsync(
            admin, channel, Article, article,
            new() { [Hero] = MissingId, [Gallery] = new[] { MissingId }, ["status"] = "published" },
            translation: new() { [OgImage] = MissingId });

        AssertWritten(outcome);
        var stored = await h.StoredLinksAsync(article);
        stored.Status.Should().Be("published");
        stored.Should().BeEquivalentTo(
            new ArticleLinks("published", "dangling", missing, [], missing, [missing], missing));
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_trashed_file_is_not_a_valid_new_assignment_but_an_unchanged_one_is_accepted(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var file = await h.SeedFileAsync();
        var article = await SeedArticleAsync(admin);
        AssertWritten(await SendAsync(admin, Rest, Article, article, new() { [Hero] = file.ToString() }));
        await TrashAsync(admin, "file", file);
        var other = await SeedArticleAsync(admin);

        var assigned = await SendFieldAsync(admin, channel, other, Hero, file.ToString());
        var unchanged = await SendFieldAsync(admin, channel, article, Hero, file.ToString(), status: "published");

        AssertRefused(assigned, MessageOf(Hero));
        AssertWritten(unchanged);
        (await h.ArticleAsync(other)).HeroImageId.Should().BeNull();
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Null_and_an_empty_list_clear_the_file_fields(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var file = await h.SeedFileAsync();
        var article = await SeedArticleAsync(admin);
        AssertWritten(await SendAsync(
            admin, Rest, Article, article, new() { [Hero] = file.ToString(), [Gallery] = new[] { file.ToString() } },
            translation: new() { [OgImage] = file.ToString() }));

        var outcome = await SendAsync(
            admin, channel, Article, article, new() { [Hero] = null, [Gallery] = Array.Empty<string>() },
            translation: new() { [OgImage] = null });

        AssertWritten(outcome);
        (await h.StoredLinksAsync(article)).Should().BeEquivalentTo(new ArticleLinks("draft", "seed", null, [], null, [], null));
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_revert_may_assign_rows_trashed_since_the_snapshot(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var history = await h.SeedHistoryAsync(admin);
        await TrashAsync(admin, "file", history.File1);
        await TrashAsync(admin, "fxCategory", history.Category1);

        AssertWritten(await RevertAsync(admin, channel, history.Article, RevisionOne));

        (await h.StoredLinksAsync(history.Article)).Should().BeEquivalentTo(history.First);
    }
}
