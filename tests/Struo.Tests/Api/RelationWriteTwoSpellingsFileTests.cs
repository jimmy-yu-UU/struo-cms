using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// A body may name a field in two spellings that differ in letter case; the binder takes every spelling and the
// last one wins. A caller without read on the target of a field may send it back unchanged only when every
// spelling holds the stored value. A name the write ignores (a differently spelled `translations` or many-to-many
// array) changes nothing.
[Collection("ApiIntegration")]
public sealed class RelationWriteTwoSpellingsFileTests : IClassFixture<NoPublicRelationsFixture>
{
    private const string Article = "fxArticle";
    private const string Hero = "heroImageId";
    private const string OgImage = "seoOgImageId";

    private readonly NoPublicRelationsApiFactory factory;
    private readonly RelationWriteHarness h;

    public RelationWriteTwoSpellingsFileTests(NoPublicRelationsFixture fixture)
    {
        factory = fixture.Factory;
        h = new(factory);
    }

    private async Task<(Guid Article, Guid Stored, Guid Other)> SeedLinkedAsync()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (stored, other) = (await h.SeedFileAsync(), await h.SeedFileAsync());
        var article = await SeedArticleAsync(admin);
        AssertWritten(await SendAsync(
            admin, Rest, Article, article, new() { [Hero] = stored.ToString() },
            translation: new() { [OgImage] = stored.ToString() }));
        return (article, stored, other);
    }

    private async Task AssertUnchangedAsync(Guid article, Guid stored)
    {
        (await h.ArticleAsync(article)).HeroImageId.Should().Be(stored);
        (await h.OgImageLinksToAsync(stored)).Should().Be(1);
    }

    [Theory]
    [InlineData("stored", "other")]
    [InlineData("other", "stored")]
    public async Task A_root_file_field_in_two_spellings_is_unchanged_only_when_both_hold_the_stored_id(
        string first, string second)
    {
        var (article, stored, other) = await SeedLinkedAsync();
        var client = await h.CallerAsync([Article], [Article]);
        string Value(string kind) => (kind == "stored" ? stored : other).ToString();

        var outcome = await SendAsync(
            client, Rest, Article, article, new() { ["heroImageId"] = Value(first), ["HeroImageId"] = Value(second) });

        AssertRefusedAlike([outcome], Rest, "file");
        await AssertUnchangedAsync(article, stored);
    }

    [Fact]
    public async Task A_root_file_field_in_two_spellings_both_holding_the_stored_id_is_accepted()
    {
        var (article, stored, _) = await SeedLinkedAsync();
        var client = await h.CallerAsync([Article], [Article]);

        var outcome = await SendAsync(
            client, Rest, Article, article,
            new() { ["heroImageId"] = stored.ToString(), ["HeroImageId"] = stored.ToString(), ["status"] = "published" });

        AssertWritten(outcome);
        (await h.ArticleAsync(article)).Status.Should().Be("published");
    }

    [Theory]
    [InlineData("stored", "other")]
    [InlineData("other", "stored")]
    public async Task A_translated_file_field_in_two_spellings_is_unchanged_only_when_both_hold_the_stored_id(
        string first, string second)
    {
        var (article, stored, other) = await SeedLinkedAsync();
        var client = await h.CallerAsync([Article], [Article]);
        string Value(string kind) => (kind == "stored" ? stored : other).ToString();

        var outcome = await SendAsync(
            client, Rest, Article, article, [],
            translation: new() { ["seoOgImageId"] = Value(first), ["SeoOgImageId"] = Value(second) });

        AssertRefusedAlike([outcome], Rest, "file");
        await AssertUnchangedAsync(article, stored);
    }

    [Fact]
    public async Task A_locale_named_in_two_spellings_is_unchanged_only_when_both_hold_the_stored_id()
    {
        var (article, stored, other) = await SeedLinkedAsync();
        var client = await h.CallerAsync([Article], [Article]);

        var outcome = await SendAsync(client, Rest, Article, article, new()
        {
            ["translations"] = new Dictionary<string, object?>
            {
                ["en"] = new Dictionary<string, object?> { [OgImage] = stored.ToString() },
                ["EN"] = new Dictionary<string, object?> { [OgImage] = other.ToString() },
            },
        });

        AssertRefusedAlike([outcome], Rest, "file");
        await AssertUnchangedAsync(article, stored);
    }

    // The write reads `translations` by its exact name, so another spelling is ignored: it neither reaches the
    // guard nor the store.
    [Fact]
    public async Task A_differently_spelled_translations_object_is_ignored()
    {
        var (article, stored, other) = await SeedLinkedAsync();
        var client = await h.CallerAsync([Article], [Article]);

        var outcome = await SendAsync(client, Rest, Article, article, new()
        {
            ["Translations"] = new Dictionary<string, object?>
            {
                ["en"] = new Dictionary<string, object?> { [OgImage] = other.ToString() },
            },
            ["status"] = "published",
        });

        AssertWritten(outcome);
        (await h.ArticleAsync(article)).Status.Should().Be("published");
        await AssertUnchangedAsync(article, stored);
        (await h.OgImageLinksToAsync(other)).Should().Be(0);
    }

    [Theory]
    [InlineData("categoryId")]
    [InlineData("CategoryId")]
    public async Task A_many_to_one_key_is_seen_by_the_guard_in_any_spelling(string name)
    {
        var (article, _, _) = await SeedLinkedAsync();
        var category = await h.SeedCategoryAsync();
        var client = await h.CallerAsync([Article], [Article]);

        var outcome = await SendAsync(client, Rest, Article, article, new() { [name] = category.ToString() });

        AssertRefusedAlike([outcome], Rest, "fxCategory");
        (await h.ArticleAsync(article)).CategoryId.Should().BeNull();
    }

    [Fact]
    public async Task A_many_to_one_key_in_two_spellings_is_refused()
    {
        var (article, _, _) = await SeedLinkedAsync();
        var category = await h.SeedCategoryAsync();
        var client = await h.CallerAsync([Article], [Article]);

        var outcome = await SendAsync(
            client, Rest, Article, article,
            new() { ["categoryId"] = null, ["CategoryId"] = category.ToString() });

        AssertRefusedAlike([outcome], Rest, "fxCategory");
        (await h.ArticleAsync(article)).CategoryId.Should().BeNull();
    }

    [Fact]
    public async Task A_many_to_many_array_in_two_spellings_is_refused_when_the_exact_name_is_present()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (tag, other) = (await h.SeedTagAsync(), await h.SeedTagAsync());
        var article = await SeedArticleAsync(admin, tag);
        var client = await h.CallerAsync([Article], [Article]);

        var outcome = await SendAsync(
            client, Rest, Article, article,
            new() { ["tags"] = Array.Empty<string>(), ["Tags"] = new[] { other.ToString() } });

        AssertRefusedAlike([outcome], Rest, "fxTag");
        (await h.LinkCountAsync(article)).Should().Be(1);
    }

    // The relation sync reads the array by its exact name, so a differently spelled array is ignored.
    [Fact]
    public async Task A_differently_spelled_many_to_many_array_alone_changes_nothing()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var (tag, other) = (await h.SeedTagAsync(), await h.SeedTagAsync());
        var article = await SeedArticleAsync(admin, tag);
        var client = await h.CallerAsync([Article], [Article]);

        var outcome = await SendAsync(
            client, Rest, Article, article, new() { ["Tags"] = new[] { other.ToString() }, ["status"] = "published" });

        AssertWritten(outcome);
        (await h.LinksToTagAsync(tag)).Should().Be(1);
        (await h.LinksToTagAsync(other)).Should().Be(0);
    }
}
