using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// A revert replays a snapshot the system stored, so a foreign key or file id whose row has been purged since is
// stored as the snapshot holds it; an ordinary update naming the same id is a 400. A many-to-many id keeps the
// existence check of the relation sync, on a revert as on an update.
[Collection("ApiIntegration")]
public class RevertPurgedLinkTests(ApiFactory factory)
{
    private const string Article = "fxArticle";
    private const string Admin = "admin";
    private const string Reader = "reader";
    private const string CategoryTarget = "category";
    private const string FileTarget = "file";

    private readonly RelationWriteHarness h = new(factory);

    private async Task<HttpClient> RevertingCallerAsync(string caller) => caller == Admin
        ? await factory.CreateAuthenticatedClientAsync()
        : await h.CallerAsync([Article, "fxTag", "fxCategory", "file"], [Article, "fxArticleTag"]);

    [Theory]
    [InlineData(Rest, Admin, CategoryTarget)]
    [InlineData(Rest, Reader, CategoryTarget)]
    [InlineData(Rest, Admin, FileTarget)]
    [InlineData(Rest, Reader, FileTarget)]
    [InlineData(Gql, Admin, CategoryTarget)]
    [InlineData(Gql, Reader, CategoryTarget)]
    [InlineData(Gql, Admin, FileTarget)]
    [InlineData(Gql, Reader, FileTarget)]
    public async Task A_revert_stores_the_snapshots_id_of_a_purged_row(string channel, string caller, string purged)
    {
        var history = await h.SeedHistoryAsync(await factory.CreateAuthenticatedClientAsync());
        if (purged == CategoryTarget) await h.PurgeAsync(category: history.Category1);
        else await h.PurgeAsync(file: history.File1);
        var client = await RevertingCallerAsync(caller);

        AssertWritten(await RevertAsync(client, channel, history.Article, RevisionOne));

        (await h.StoredLinksAsync(history.Article)).Should().BeEquivalentTo(history.First);
    }

    [Theory]
    [InlineData("categoryId", CategoryTarget)]
    [InlineData("heroImageId", FileTarget)]
    [InlineData("gallery", FileTarget)]
    [InlineData("seoOgImageId", FileTarget)]
    public async Task An_update_naming_the_same_purged_id_is_refused(string field, string purged)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var history = await h.SeedHistoryAsync(admin);
        await h.PurgeAsync(category: history.Category1, file: history.File1);
        var gone = purged == CategoryTarget ? history.Category1 : history.File1;
        object value = field == "gallery" ? new[] { gone.ToString() } : gone.ToString();

        var outcome = field == "seoOgImageId"
            ? await SendAsync(admin, Rest, Article, history.Article, [], translation: new() { [field] = value })
            : await SendAsync(admin, Rest, Article, history.Article, new() { [field] = value });

        outcome.Code.Should().Be("BAD_USER_INPUT", outcome.Body);
        (await h.StoredLinksAsync(history.Article)).Should().BeEquivalentTo(history.Second);
    }

    [Theory]
    [InlineData(Rest, Admin)]
    [InlineData(Rest, Reader)]
    [InlineData(Gql, Admin)]
    [InlineData(Gql, Reader)]
    public async Task A_revert_to_a_purged_many_to_many_target_is_refused(string channel, string caller)
    {
        var history = await h.SeedHistoryAsync(await factory.CreateAuthenticatedClientAsync());
        await h.PurgeAsync(tag: history.Tag1);
        var client = await RevertingCallerAsync(caller);

        var outcome = await RevertAsync(client, channel, history.Article, RevisionOne);

        outcome.Code.Should().Be("BAD_USER_INPUT", outcome.Body);
        outcome.Message.Should().Be("One or more ids in 'tags' do not exist in 'fxTag'.");
        (await h.StoredLinksAsync(history.Article)).Should().BeEquivalentTo(history.Second);
    }
}
