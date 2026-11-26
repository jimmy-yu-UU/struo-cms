using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// Behaviour of the relation write checks that is pinned as it stands: the answer for a missing item does not
// depend on read access, and an empty Guid in a link key is stored as sent.
[Collection("ApiIntegration")]
public sealed class LinkExistencePinnedTests : IClassFixture<NoPublicRelationsFixture>
{
    private const string Article = "fxArticle";
    private const string Hero = "heroImageId";
    private const string Category = "categoryId";

    private readonly NoPublicRelationsApiFactory factory;
    private readonly RelationWriteHarness h;

    public LinkExistencePinnedTests(NoPublicRelationsFixture fixture)
    {
        factory = fixture.Factory;
        h = new(factory);
    }

    [Fact]
    public async Task A_graphql_update_of_a_missing_item_answers_null_without_errors_whether_or_not_the_caller_reads_files()
    {
        var file = (await h.SeedFileAsync()).ToString();
        var withoutRead = await h.CallerAsync([Article], [Article]);
        var withRead = await h.CallerAsync([Article, "file"], [Article]);
        var missing = Guid.NewGuid();

        var outcomes = new List<WriteOutcome>
        {
            await h.SendAsync(withoutRead, Gql, Article, missing, new() { [Hero] = file }),
            await h.SendAsync(withRead, Gql, Article, missing, new() { [Hero] = file }),
        };

        foreach (var outcome in outcomes)
        {
            outcome.Status.Should().Be(System.Net.HttpStatusCode.OK);
            var root = JsonDocument.Parse(outcome.Body).RootElement;
            root.TryGetProperty("errors", out _).Should().BeFalse(outcome.Body);
            root.GetProperty("data").GetProperty("updateFxArticle").ValueKind.Should().Be(JsonValueKind.Null);
        }
    }

    [Theory]
    [InlineData(Post)]
    [InlineData(Put)]
    public async Task An_empty_guid_in_a_foreign_key_and_in_a_file_field_is_stored_as_sent(string op)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var client = await h.CallerAsync([Article, "fxCategory", "file"], [Article]);
        var id = op == Put ? await h.SeedArticleAsync(admin) : (Guid?)null;
        var fields = new Dictionary<string, object?> { [Category] = Guid.Empty.ToString(), [Hero] = Guid.Empty.ToString() };

        var outcome = await h.SendAsync(client, Rest, Article, id, fields, translated: true);

        AssertWritten(outcome);
        var written = id ?? Guid.Parse(JsonDocument.Parse(outcome.Body).RootElement.GetProperty("data").GetProperty("id").GetString()!);
        var stored = await h.ArticleAsync(written);
        stored.CategoryId.Should().Be(Guid.Empty);
        stored.HeroImageId.Should().Be(Guid.Empty);
    }
}
