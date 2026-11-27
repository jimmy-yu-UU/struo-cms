using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// A junction row read back carries both keys, so a client that edits its payload sends them again. On an update
// a junction key that equals the stored value links nothing new and needs neither read on its target nor an
// existence check; a changed key, and every key of a create, is checked as before.
[Collection("ApiIntegration")]
public class RelationWriteJunctionUnchangedKeyTests(ApiFactory factory)
{
    private const string ArticleTag = "fxArticleTag";
    private const string Article = "fxArticle";
    private const string Tag = "fxTag";
    private const string MissingTag = "The id in 'tagId' does not exist in 'fxTag'.";

    private readonly RelationWriteHarness h = new(factory);

    private static Dictionary<string, object?> Keys(Guid article, Guid tag, string note) =>
        new() { ["articleId"] = article.ToString(), ["tagId"] = tag.ToString(), ["note"] = note };

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Both_keys_sent_back_unchanged_store_the_payload_without_any_target_read(string channel)
    {
        var client = await h.CallerAsync([], [ArticleTag]);
        var article = await SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());
        var tag = await h.SeedTagAsync();
        var link = await h.SeedLinkAsync(article, tag);

        var outcome = await SendAsync(client, channel, ArticleTag, link, Keys(article, tag, "edited"));

        AssertWritten(outcome);
        var stored = await h.LinkAsync(link);
        (stored.Note, stored.ArticleId, stored.TagId).Should().Be(("edited", article, tag));
    }

    [Fact]
    public async Task Keys_are_matched_ignoring_the_case_of_the_name_and_of_the_id_text()
    {
        var client = await h.CallerAsync([], [ArticleTag]);
        var article = await SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());
        var tag = await h.SeedTagAsync();
        var link = await h.SeedLinkAsync(article, tag);

        var outcome = await SendAsync(
            client, Rest, ArticleTag, link,
            new() { ["TagId"] = tag.ToString().ToUpperInvariant(), ["ArticleId"] = article.ToString().ToUpperInvariant(), ["note"] = "n" });

        AssertWritten(outcome);
        (await h.LinkAsync(link)).Note.Should().Be("n");
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task One_key_changed_to_a_row_the_caller_cannot_read_is_refused(string channel)
    {
        var client = await h.CallerAsync([], [ArticleTag]);
        var article = await SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());
        var tag = await h.SeedTagAsync();
        var other = await h.SeedTagAsync();
        var link = await h.SeedLinkAsync(article, tag);

        var outcome = await SendAsync(client, channel, ArticleTag, link, Keys(article, other, "edited"));

        AssertRefusedAlike([outcome], channel, Tag);
        var stored = await h.LinkAsync(link);
        stored.Note.Should().BeNull();
        stored.TagId.Should().Be(tag);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task One_key_changed_to_a_readable_missing_id_is_a_bad_request(string channel)
    {
        var client = await h.CallerAsync([Tag], [ArticleTag]);
        var article = await SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());
        var tag = await h.SeedTagAsync();
        var link = await h.SeedLinkAsync(article, tag);

        var outcome = await SendAsync(
            client, channel, ArticleTag, link, Keys(article, Guid.Parse(IdOf(Missing, tag)), "edited"));

        outcome.Code.Should().Be("BAD_USER_INPUT", outcome.Body);
        outcome.Message.Should().Be(MissingTag);
        (await h.LinkAsync(link)).TagId.Should().Be(tag);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_create_with_both_keys_still_needs_read_on_the_targets(string channel)
    {
        var client = await h.CallerAsync([], [ArticleTag]);
        var article = await SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());
        var tag = await h.SeedTagAsync();
        var rows = await h.LinkRowCountAsync();

        var outcome = await SendAsync(client, channel, ArticleTag, null, Keys(article, tag, "n"));

        AssertRefusedAlike([outcome], channel, Tag);
        (await h.LinkRowCountAsync()).Should().Be(rows);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_caller_who_reads_the_targets_is_unaffected(string channel)
    {
        var client = await h.CallerAsync([Article, Tag], [ArticleTag]);
        var article = await SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());
        var tag = await h.SeedTagAsync();
        var other = await h.SeedTagAsync();
        var link = await h.SeedLinkAsync(article, tag);

        AssertWritten(await SendAsync(client, channel, ArticleTag, link, Keys(article, tag, "same")));
        AssertWritten(await SendAsync(client, channel, ArticleTag, link, Keys(article, other, "moved")));

        var stored = await h.LinkAsync(link);
        (stored.Note, stored.TagId).Should().Be(("moved", other));
    }
}
