using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// A body that names a key twice in different letter case is accepted by the unique-name check (names are
// compared exactly) and bound ignoring case. Every spelling must pass each exemption, so a caller without read
// on a key's target cannot move a junction row, nor probe an id, by hiding the new value behind the stored one.
[Collection("ApiIntegration")]
public class RelationWriteTwoSpellingsTests(ApiFactory factory)
{
    private const string ArticleTag = "fxArticleTag";
    private const string Tag = "fxTag";

    private readonly RelationWriteHarness h = new(factory);

    public static TheoryData<string, string, string, string> Orders() => new()
    {
        { "tagId", "TagId", "stored", "other" },
        { "TagId", "tagId", "stored", "other" },
        { "tagId", "TagId", "other", "stored" },
        { "TagId", "tagId", "other", "stored" },
        { "tagId", "TagId", "stored", "missing" },
        { "TagId", "tagId", "stored", "missing" },
        { "tagId", "TagId", "missing", "stored" },
        { "TagId", "tagId", "missing", "stored" },
    };

    private static string ValueOf(string kind, Guid stored, Guid other) => kind switch
    {
        "stored" => stored.ToString(),
        "other" => other.ToString(),
        _ => IdOf(Missing, stored),
    };

    private static string TagName(Guid actual, Guid original, Guid other)
    {
        if (actual == original) return "original";
        return actual == other ? "other" : "?";
    }

    [Theory]
    [MemberData(nameof(Orders))]
    public async Task A_junction_key_in_two_spellings_is_unchanged_only_when_every_spelling_is(
        string firstName, string secondName, string first, string second)
    {
        var client = await h.CallerAsync([], [ArticleTag]);
        var article = await SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());
        var tag = await h.SeedTagAsync();
        var other = await h.SeedTagAsync();
        var link = await h.SeedLinkAsync(article, tag);

        var outcome = await SendAsync(client, Rest, ArticleTag, link, new()
        {
            ["articleId"] = article.ToString(),
            [firstName] = ValueOf(first, tag, other),
            [secondName] = ValueOf(second, tag, other),
            ["note"] = "edited",
        });

        var stored = await h.LinkAsync(link);
        var seen = $"{(int)outcome.Status} {outcome.Code}; tag {TagName(stored.TagId, tag, other)}; note {stored.Note ?? "null"}";
        seen.Should().Be("403 FORBIDDEN; tag original; note null");
        AssertRefusedAlike([outcome], Rest, Tag);
    }

    [Fact]
    public async Task A_junction_key_in_two_spellings_that_both_hold_the_stored_value_is_unchanged()
    {
        var client = await h.CallerAsync([], [ArticleTag]);
        var article = await SeedArticleAsync(await factory.CreateAuthenticatedClientAsync());
        var tag = await h.SeedTagAsync();
        var link = await h.SeedLinkAsync(article, tag);

        var outcome = await SendAsync(client, Rest, ArticleTag, link, new()
        {
            ["articleId"] = article.ToString(), ["tagId"] = tag.ToString(), ["TagId"] = tag.ToString(), ["note"] = "edited",
        });

        AssertWritten(outcome);
        (await h.LinkAsync(link)).Note.Should().Be("edited");
    }
}
