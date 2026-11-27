using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;
using static Struo.Tests.Api.RelationWriteHarness;

namespace Struo.Tests.Api;

// Removing a whole locale takes the file links inside it with it, whether an update removes it or a revert to a
// snapshot that lacks it does, and neither needs read on the file collection.
[Collection("ApiIntegration")]
public class LocaleRemovalWithoutFileReadTests(ApiFactory factory)
{
    private const string Article = "fxArticle";
    private const string Locale = "zh-TW";

    private readonly RelationWriteHarness h = new(factory);

    private async Task<Guid> SeedWithLocaleAsync(HttpClient admin, Guid file)
    {
        var article = await h.SeedArticleAsync(admin);
        var resp = await admin.PutAsJsonAsync($"/api/items/{Article}/{article}", new
        {
            translations = new Dictionary<string, object>
            {
                [Locale] = new { title = "zh", seoOgImageId = file },
            },
        });
        resp.EnsureSuccessStatusCode();
        (await h.TranslationRowsAsync(article, Locale)).Should().Be(1);
        return article;
    }

    [Fact]
    public async Task An_update_removing_a_locale_that_holds_a_file_link_succeeds_without_file_read()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var file = await h.SeedFileAsync();
        var article = await SeedWithLocaleAsync(admin, file);
        var client = await h.CallerAsync([Article], [Article]);

        var resp = await client.PutAsJsonAsync(
            $"/api/items/{Article}/{article}", JsonDocument.Parse("{\"translations\":{\"zh-TW\":null}}").RootElement);

        resp.IsSuccessStatusCode.Should().BeTrue(await resp.Content.ReadAsStringAsync());
        (await h.TranslationRowsAsync(article, Locale)).Should().Be(0);
        (await h.OgImageLinksToAsync(file)).Should().Be(0);
    }

    [Theory]
    [InlineData(Rest)]
    [InlineData(Gql)]
    public async Task A_revert_to_a_snapshot_without_the_locale_succeeds_without_file_read(string channel)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var file = await h.SeedFileAsync();
        var article = await SeedWithLocaleAsync(admin, file);
        var client = await h.CallerAsync([Article], [Article]);

        AssertWritten(await h.RevertAsync(client, channel, article, RevisionOne));

        (await h.TranslationRowsAsync(article, Locale)).Should().Be(0);
        (await h.OgImageLinksToAsync(file)).Should().Be(0);
    }
}
