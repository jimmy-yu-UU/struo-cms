using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// A revision snapshot shows a relation key only where the item read would: the many-to-many arrays and foreign
// keys of a relation whose target the caller cannot read are left out. File ids stay, as the item read returns
// them. The stored snapshot is not changed.
[Collection("ApiIntegration")]
public sealed class RevisionSnapshotRelationReadTests : IClassFixture<NoPublicRelationsFixture>
{
    private const string Article = "fxArticle";
    private const string Tags = "tags";
    private const string CategoryId = "categoryId";
    private const string Junction = "fxArticleTag";

    private readonly NoPublicRelationsApiFactory factory;
    private readonly RelationWriteHarness h;

    public RevisionSnapshotRelationReadTests(NoPublicRelationsFixture fixture)
    {
        factory = fixture.Factory;
        h = new(factory);
    }

    private static void AssertFileIdsKept(JsonElement snapshot, ArticleHistory history)
    {
        snapshot.GetProperty("heroImageId").GetGuid().Should().Be(history.File1);
        snapshot.GetProperty("gallery").EnumerateArray().Select(e => e.GetGuid()).Should().Equal(history.File1);
        snapshot.GetProperty("translations").GetProperty("en").GetProperty("seoOgImageId").GetGuid()
            .Should().Be(history.File1);
        snapshot.GetProperty("status").GetString().Should().Be("draft");
    }

    private static void AssertRelationsShown(JsonElement snapshot, ArticleHistory history)
    {
        snapshot.GetProperty(CategoryId).GetGuid().Should().Be(history.Category1);
        snapshot.GetProperty(Tags).EnumerateArray().Select(t => t.GetProperty("id").GetGuid()).Should().Equal(history.Tag1);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_caller_without_read_on_the_targets_sees_no_relation_key(string channel)
    {
        var history = await h.SeedHistoryAsync(await factory.CreateAuthenticatedClientAsync());
        var client = await h.CallerAsync([Article], []);

        var snapshot = await SnapshotAsync(client, channel, history.Article, RevisionOne);

        snapshot.TryGetProperty(Tags, out _).Should().BeFalse();
        snapshot.TryGetProperty(CategoryId, out _).Should().BeFalse();
        AssertFileIdsKept(snapshot, history);
    }

    [Theory]
    [InlineData(Rest, Tags)]
    [InlineData(Rest, CategoryId)]
    [InlineData(Gql, Tags)]
    [InlineData(Gql, CategoryId)]
    public async Task Only_the_relation_whose_target_the_caller_cannot_read_is_left_out(string channel, string unreadable)
    {
        var history = await h.SeedHistoryAsync(await factory.CreateAuthenticatedClientAsync());
        var client = await h.CallerAsync([Article, unreadable == Tags ? "fxCategory" : "fxTag"], []);

        var snapshot = await SnapshotAsync(client, channel, history.Article, RevisionOne);

        snapshot.TryGetProperty(unreadable, out _).Should().BeFalse();
        var shown = unreadable == Tags ? CategoryId : Tags;
        snapshot.TryGetProperty(shown, out _).Should().BeTrue();
        AssertFileIdsKept(snapshot, history);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_caller_who_reads_every_target_and_a_super_admin_see_the_full_snapshot(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var history = await h.SeedHistoryAsync(admin);
        var reader = await h.CallerAsync([Article, "fxCategory", "fxTag", Junction], []);

        var asReader = await SnapshotAsync(reader, channel, history.Article, RevisionOne);
        var asAdmin = await SnapshotAsync(admin, channel, history.Article, RevisionOne);

        AssertRelationsShown(asReader, history);
        AssertRelationsShown(asAdmin, history);
        AssertFileIdsKept(asReader, history);
        asReader.GetRawText().Should().Be(asAdmin.GetRawText());
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_caller_who_reads_the_target_but_not_the_junction_sees_the_ids_without_the_payload(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var history = await h.SeedHistoryAsync(admin);
        var client = await h.CallerAsync([Article, "fxCategory", "fxTag"], []);

        var snapshot = await SnapshotAsync(client, channel, history.Article, RevisionOne);

        var tags = snapshot.GetProperty(Tags).EnumerateArray().ToList();
        tags.Should().OnlyContain(t => t.ValueKind == JsonValueKind.String);
        tags.Select(t => t.GetGuid()).Should().Equal(history.Tag1);
        snapshot.GetProperty(CategoryId).GetGuid().Should().Be(history.Category1);
        AssertFileIdsKept(snapshot, history);
    }

    [Fact]
    public async Task A_caller_who_reads_the_junction_but_not_the_target_sees_no_array()
    {
        var history = await h.SeedHistoryAsync(await factory.CreateAuthenticatedClientAsync());
        var client = await h.CallerAsync([Article, "fxCategory", Junction], []);

        var snapshot = await SnapshotAsync(client, Rest, history.Article, RevisionOne);

        snapshot.TryGetProperty(Tags, out _).Should().BeFalse();
    }

    [Fact]
    public async Task The_stored_snapshot_is_unchanged_by_a_restricted_read()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var history = await h.SeedHistoryAsync(admin);
        var client = await h.CallerAsync([Article], []);
        await SnapshotAsync(client, Rest, history.Article, RevisionOne);

        var asAdmin = await SnapshotAsync(admin, Rest, history.Article, RevisionOne);

        AssertRelationsShown(asAdmin, history);
    }
}
