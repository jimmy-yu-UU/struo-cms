using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// A structured value on a Hidden-interface field is written and read as the JSON value it is.
[Collection("ApiIntegration")]
public sealed class HiddenStructuredFieldApiTests(ApiFactory factory)
{
    private const string Items = "/api/items/fxStash";
    private const string Glossaries = "/api/items/fxGlossary";

    private static readonly string[] LabelsAb = ["a", "b"];
    private static readonly string[] LabelsCde = ["c", "d", "e"];
    private static readonly int[] NumbersOneTwo = [1, 2];
    private static readonly int[] NumbersThreeToFive = [3, 4, 5];
    private static readonly string[] AliasesEn = ["a", "b"];
    private static readonly string[] AliasesZh = ["\u7532", "\u4e59", "\u4e19"];
    private static readonly string[] AliasesEnUpdated = ["c"];
    private static readonly string[] AliasesZhUpdated = ["\u4e01", "\u620a"];

    private static async Task<JsonElement> DataOf(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.GetProperty("data").Clone();
    }

    private static async Task<string> CreateItemAsync(HttpClient client, object body, string items = Items)
    {
        var create = await client.PostAsJsonAsync(items, body);
        create.StatusCode.Should().Be(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        return (await DataOf(create)).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string id, string items = Items)
    {
        var get = await client.GetAsync($"{items}/{id}");
        get.StatusCode.Should().Be(HttpStatusCode.OK);
        return await DataOf(get);
    }

    private static async Task UpdateAsync(HttpClient client, string id, object body, string items = Items)
    {
        var update = await client.PutAsJsonAsync($"{items}/{id}", body);
        update.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static string[] Strings(JsonElement value) => value.EnumerateArray().Select(e => e.GetString()!).ToArray();

    private static int[] Ints(JsonElement value) => value.EnumerateArray().Select(e => e.GetInt32()).ToArray();

    private static Dictionary<string, int> Scores(JsonElement value) =>
        value.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetInt32());

    [Fact]
    public async Task A_list_on_a_Hidden_field_reads_back_after_create_and_after_update()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateItemAsync(client, new { name = "s", labels = LabelsAb });

        Strings((await ReadAsync(client, id)).GetProperty("labels")).Should().Equal("a", "b");

        await UpdateAsync(client, id, new { labels = LabelsCde });

        Strings((await ReadAsync(client, id)).GetProperty("labels")).Should().Equal("c", "d", "e");
    }

    [Fact]
    public async Task An_array_on_a_Hidden_field_reads_back_after_create_and_after_update()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateItemAsync(client, new { name = "s", numbers = NumbersOneTwo });

        Ints((await ReadAsync(client, id)).GetProperty("numbers")).Should().Equal(1, 2);

        await UpdateAsync(client, id, new { numbers = NumbersThreeToFive });

        Ints((await ReadAsync(client, id)).GetProperty("numbers")).Should().Equal(3, 4, 5);
    }

    [Fact]
    public async Task A_dictionary_on_a_Hidden_field_reads_back_after_create_and_after_update()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateItemAsync(client, new { name = "s", scores = new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 } });

        Scores((await ReadAsync(client, id)).GetProperty("scores"))
            .Should().BeEquivalentTo(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });

        await UpdateAsync(client, id, new { scores = new Dictionary<string, int> { ["c"] = 3 } });

        Scores((await ReadAsync(client, id)).GetProperty("scores"))
            .Should().BeEquivalentTo(new Dictionary<string, int> { ["c"] = 3 });
    }

    [Fact]
    public async Task A_custom_class_on_a_Hidden_field_reads_back_after_create_and_after_update()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateItemAsync(client, new { name = "s", note = new { text = "first", weight = 1 } });

        var first = (await ReadAsync(client, id)).GetProperty("note");
        first.GetProperty("text").GetString().Should().Be("first");
        first.GetProperty("weight").GetInt32().Should().Be(1);

        await UpdateAsync(client, id, new { note = new { text = "second", weight = 2 } });

        var second = (await ReadAsync(client, id)).GetProperty("note");
        second.GetProperty("text").GetString().Should().Be("second");
        second.GetProperty("weight").GetInt32().Should().Be(2);
    }

    [Fact]
    public async Task A_list_on_a_translatable_Hidden_field_reads_back_per_locale_after_create_and_after_update()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateItemAsync(client, new
        {
            translations = new Dictionary<string, object>
            {
                ["en"] = new { title = "T", aliases = AliasesEn },
                ["zh-TW"] = new { title = "T", aliases = AliasesZh },
            },
        }, Glossaries);

        var first = (await ReadAsync(client, id, Glossaries)).GetProperty("translations");
        Strings(first.GetProperty("en").GetProperty("aliases")).Should().Equal("a", "b");
        Strings(first.GetProperty("zh-TW").GetProperty("aliases")).Should().Equal("\u7532", "\u4e59", "\u4e19");

        await UpdateAsync(client, id, new
        {
            translations = new Dictionary<string, object>
            {
                ["en"] = new { aliases = AliasesEnUpdated },
                ["zh-TW"] = new { aliases = AliasesZhUpdated },
            },
        }, Glossaries);

        var second = (await ReadAsync(client, id, Glossaries)).GetProperty("translations");
        Strings(second.GetProperty("en").GetProperty("aliases")).Should().Equal("c");
        Strings(second.GetProperty("zh-TW").GetProperty("aliases")).Should().Equal("\u4e01", "\u620a");
    }
}
