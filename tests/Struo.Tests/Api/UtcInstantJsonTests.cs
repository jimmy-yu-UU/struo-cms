using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// REST reads and writes every <c>DateTime</c> as a UTC instant: responses end in Z on every backend,
/// and a written value with an offset is stored as that instant.
/// </summary>
[Collection("ApiIntegration")]
public class UtcInstantJsonTests(ApiFactory factory)
{
    [Fact]
    public async Task MediaFolder_createdAt_is_serialised_as_the_utc_instant()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var before = DateTime.UtcNow;
        var created = await admin.PostAsJsonAsync("/api/items/mediaFolder", new { name = "utc-json-probe" });
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("id").GetString();

        var body = await admin.GetStringAsync($"/api/items/mediaFolder/{id}");
        var createdAt = JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("createdAt").GetString()!;

        createdAt.Should().EndWith("Z");
        DateTime.Parse(createdAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            .Should().BeCloseTo(before, TimeSpan.FromMinutes(1));
    }

    [Fact]
    public async Task Written_offset_value_is_stored_as_its_instant_and_found_by_that_instant()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var created = await admin.PostAsJsonAsync("/api/items/fxArticle", new
        {
            status = "draft",
            publishedAt = "2026-10-14T10:00:00+08:00",
            translations = new { en = new { title = "utc-offset-probe" } }
        });
        created.StatusCode.Should().Be(System.Net.HttpStatusCode.Created, await created.Content.ReadAsStringAsync());
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var body = await admin.GetStringAsync($"/api/items/fxArticle/{id}");
        JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("publishedAt").GetString()
            .Should().Be("2026-10-14T02:00:00Z");

        var found = await admin.GetStringAsync(
            "/api/items/fxArticle?filter[publishedAt][_eq]=2026-10-14T02:00:00Z&limit=500");
        JsonDocument.Parse(found).RootElement.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()).Should().Contain(id);
    }

    [Fact]
    public async Task GraphQl_mutation_with_an_offset_value_stores_the_same_instant()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var response = await admin.PostAsJsonAsync("/graphql", new
        {
            query = "mutation { createFxArticle(input: { status: \"draft\", publishedAt: \"2026-10-14T10:00:00+08:00\", " +
                    "translations: [ { locale: \"en\", fields: { title: \"utc-gql-probe\" } } ] }) { id } }"
        });
        var root = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        root.TryGetProperty("errors", out var errors).Should().BeFalse($"unexpected errors: {errors}");
        var id = root.GetProperty("data").GetProperty("createFxArticle").GetProperty("id").GetString()!;

        var body = await admin.GetStringAsync($"/api/items/fxArticle/{id}");
        JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("publishedAt").GetString()
            .Should().Be("2026-10-14T02:00:00Z");
    }

    [Fact]
    public async Task A_new_revision_snapshot_carries_dates_ending_in_Z()
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var created = await admin.PostAsJsonAsync("/api/items/fxArticle", new
        {
            status = "draft",
            publishedAt = "2026-10-14T10:00:00+08:00",
            translations = new { en = new { title = "utc-snapshot-probe" } }
        });
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("id").GetString()!;

        var body = await admin.GetStringAsync($"/api/items/fxArticle/{id}/revisions/1");
        JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("snapshot")
            .GetProperty("publishedAt").GetString().Should().Be("2026-10-14T02:00:00Z");
    }

    // A query-string _in/_nin arrives as one comma-separated string; each element is an instant.
    [Theory]
    [InlineData("utc")]
    [InlineData("unspecified")]
    public async Task Query_string_in_and_nin_compare_each_element_as_an_instant(string form)
    {
        var admin = await factory.CreateAuthenticatedClientAsync();
        var a = await CreateFolderAsync(admin, "in-a");
        var b = await CreateFolderAsync(admin, "in-b");
        var list = string.Join(",", new[] { a, b }.Select(f => Format(f.CreatedAt, form)));

        var inIds = await FolderIdsAsync(admin, "_in", list);
        var ninIds = await FolderIdsAsync(admin, "_nin", list);

        inIds.Should().BeEquivalentTo([a.Id, b.Id]);
        ninIds.Should().NotContain(a.Id).And.NotContain(b.Id);
    }

    private static async Task<(string Id, DateTime CreatedAt)> CreateFolderAsync(HttpClient admin, string name)
    {
        var created = await admin.PostAsJsonAsync("/api/items/mediaFolder", new { name });
        var id = JsonDocument.Parse(await created.Content.ReadAsStringAsync())
            .RootElement.GetProperty("data").GetProperty("id").GetString()!;
        var body = await admin.GetStringAsync($"/api/items/mediaFolder/{id}");
        var createdAt = JsonDocument.Parse(body).RootElement.GetProperty("data").GetProperty("createdAt").GetString()!;
        return (id, DateTime.Parse(createdAt, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));
    }

    private static string Format(DateTime utc, string form) => form == "utc"
        ? utc.ToString("O", CultureInfo.InvariantCulture)
        : utc.ToString("yyyy-MM-ddTHH:mm:ss.fffffff", CultureInfo.InvariantCulture);

    private static async Task<List<string>> FolderIdsAsync(HttpClient admin, string op, string list)
    {
        var body = await admin.GetStringAsync(
            $"/api/items/mediaFolder?limit=500&filter[createdAt][{op}]={Uri.EscapeDataString(list)}");
        return JsonDocument.Parse(body).RootElement.GetProperty("data").EnumerateArray()
            .Select(e => e.GetProperty("id").GetString()!).ToList();
    }
}
