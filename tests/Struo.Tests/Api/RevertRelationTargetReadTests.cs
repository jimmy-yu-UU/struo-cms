using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// A revert replays a snapshot. The relations whose target collection the caller cannot read keep their
// current value; everything else reverts.
[Collection("ApiIntegration")]
public sealed class RevertRelationTargetReadTests : IClassFixture<NoPublicRelationsFixture>
{
    private const string Article = "fxArticle";
    private const string Tags = "tags";
    private const string Category = "category";
    private const string Files = "files";

    private readonly NoPublicRelationsApiFactory factory;
    private readonly RelationWriteHarness h;

    public RevertRelationTargetReadTests(NoPublicRelationsFixture fixture)
    {
        factory = fixture.Factory;
        h = new(factory);
    }

    private static string[] ReadsAllBut(string unreadable)
    {
        var targets = new Dictionary<string, string> { [Tags] = "fxTag", [Category] = "fxCategory", [Files] = "file" };
        return [Article, .. targets.Where(t => t.Key != unreadable).Select(t => t.Value)];
    }

    private static ArticleLinks Expected(ArticleHistory history, string unreadable) => unreadable switch
    {
        Tags => history.First with { Tags = history.Second.Tags },
        Category => history.First with { Category = history.Second.Category },
        Files => history.First with
        {
            Hero = history.Second.Hero, Gallery = history.Second.Gallery, OgImage = history.Second.OgImage,
        },
        _ => history.First,
    };

    [Theory]
    [InlineData(Rest, Tags)]
    [InlineData(Rest, Category)]
    [InlineData(Rest, Files)]
    [InlineData(Gql, Tags)]
    [InlineData(Gql, Category)]
    [InlineData(Gql, Files)]
    public async Task Revert_keeps_the_relations_whose_target_the_caller_cannot_read(string channel, string unreadable)
    {
        var history = await h.SeedHistoryAsync(await factory.CreateAuthenticatedClientAsync());
        var client = await h.CallerAsync(ReadsAllBut(unreadable), [Article, "fxArticleTag"]);

        var outcome = await RevertAsync(client, channel, history.Article, RevisionOne);

        AssertWritten(outcome);
        (await h.StoredLinksAsync(history.Article)).Should().BeEquivalentTo(Expected(history, unreadable));
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Revert_without_read_on_any_target_reverts_only_the_other_fields(string channel)
    {
        var history = await h.SeedHistoryAsync(await factory.CreateAuthenticatedClientAsync());
        var client = await h.CallerAsync([Article], [Article]);

        var outcome = await RevertAsync(client, channel, history.Article, RevisionOne);

        AssertWritten(outcome);
        (await h.StoredLinksAsync(history.Article)).Should().BeEquivalentTo(history.Second with { Status = "draft", Title = "v1" });
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Revert_by_a_caller_who_reads_every_target_reverts_everything(string channel)
    {
        var history = await h.SeedHistoryAsync(await factory.CreateAuthenticatedClientAsync());
        var client = await h.CallerAsync(ReadsAllBut(string.Empty), [Article, "fxArticleTag"]);

        AssertWritten(await RevertAsync(client, channel, history.Article, RevisionOne));

        (await h.StoredLinksAsync(history.Article)).Should().BeEquivalentTo(history.First);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task Super_admin_reverts_everything(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var history = await h.SeedHistoryAsync(admin);

        AssertWritten(await RevertAsync(admin, channel, history.Article, RevisionOne));

        (await h.StoredLinksAsync(history.Article)).Should().BeEquivalentTo(history.First);
    }

    [Fact]
    public async Task The_revert_revision_records_the_resulting_row()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var history = await h.SeedHistoryAsync(admin);
        var client = await h.CallerAsync([Article], [Article]);

        AssertWritten(await RevertAsync(client, Rest, history.Article, RevisionOne));

        var snapshot = await SnapshotAsync(admin, Rest, history.Article, "3");
        snapshot.GetProperty("categoryId").GetGuid().Should().Be(history.Category2);
        snapshot.GetProperty("tags").EnumerateArray().Select(t => t.GetProperty("id").GetGuid())
            .Should().Equal(history.Tag2);
        snapshot.GetProperty("heroImageId").GetGuid().Should().Be(history.File2);
        snapshot.GetProperty("translations").GetProperty("en").GetProperty("seoOgImageId").GetGuid()
            .Should().Be(history.File2);
        snapshot.GetProperty("status").GetString().Should().Be("draft");
    }
}
