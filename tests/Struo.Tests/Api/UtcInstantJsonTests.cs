using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// A <c>TimestampWithTimeZone</c> value reaches API JSON as the stored UTC instant. On PostgreSQL and
/// SQL Server it reads back as <see cref="DateTimeKind.Utc"/> and serialises with a trailing Z; the
/// other backends read the UTC wall clock back without a zone.
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

        if (TestBackend.Current is TestBackendKind.PostgreSQL or TestBackendKind.SqlServer)
            createdAt.Should().EndWith("Z");
        DateTime.Parse(createdAt, CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal)
            .Should().BeCloseTo(before, TimeSpan.FromMinutes(1));
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
