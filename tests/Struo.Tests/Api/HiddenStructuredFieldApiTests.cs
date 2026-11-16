using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// A structured value on a Hidden-interface field is written and read as the JSON array it is.
[Collection("ApiIntegration")]
public sealed class HiddenStructuredFieldApiTests(ApiFactory factory)
{
    private static async Task<JsonElement> DataOf(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("data").Clone();
    }

    private static string[] Labels(JsonElement item) =>
        item.GetProperty("labels").EnumerateArray().Select(e => e.GetString()!).ToArray();

    [Fact]
    public async Task A_list_on_a_Hidden_field_reads_back_after_create_and_after_update()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var create = await client.PostAsJsonAsync("/api/items/fxStash", new { name = "s", labels = new[] { "a", "b" } });
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        var id = (await DataOf(create)).GetProperty("id").GetString();

        var first = await client.GetAsync($"/api/items/fxStash/{id}");
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        Labels(await DataOf(first)).Should().Equal("a", "b");

        var update = await client.PutAsJsonAsync($"/api/items/fxStash/{id}", new { labels = new[] { "c", "d", "e" } });
        update.StatusCode.Should().Be(HttpStatusCode.OK);

        var second = await client.GetAsync($"/api/items/fxStash/{id}");
        Labels(await DataOf(second)).Should().Equal("c", "d", "e");
    }
}
