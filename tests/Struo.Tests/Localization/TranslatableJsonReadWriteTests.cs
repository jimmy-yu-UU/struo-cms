using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Revisions;
using Struo.Tests.Fixtures;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Localization;

/// <summary>
/// A translatable <c>Json</c> field reads and writes like a root <c>Json</c> field: any non-null value is
/// stored as its raw JSON text (a string value as a JSON string), and the stored text reads back as a
/// structured value, or as the raw string when it is not JSON. The fixture article's translatable
/// <c>facts</c> is compared with its root <c>attributes</c>.
/// </summary>
[Collection("ApiIntegration")]
public class TranslatableJsonReadWriteTests(ApiFactory factory)
{
    private const string Collection = "fxArticle";
    private const string Marker = "$snapshotFormat";
    private readonly ApiFactory _factory = factory;

    private static JsonElement Root(string b) => JsonDocument.Parse(b).RootElement;

    private static StringContent Json(string raw) => new(raw, Encoding.UTF8, "application/json");

    private static async Task<string> CreateAsync(HttpClient c, string factsRaw)
    {
        var resp = await c.PostAsync($"/api/items/{Collection}", Json(
            "{\"status\":\"draft\",\"attributes\":" + factsRaw +
            ",\"translations\":{\"en\":{\"title\":\"A\",\"facts\":" + factsRaw + "}}}"));
        resp.StatusCode.Should().Be(HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> GetAsync(HttpClient c, string id) =>
        Root(await c.GetStringAsync($"/api/items/{Collection}/{id}")).GetProperty("data");

    private static JsonElement Facts(JsonElement item) =>
        item.GetProperty("translations").GetProperty("en").GetProperty("facts");

    private async Task<string?> StoredFactsAsync(string id)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var guid = Guid.Parse(id);
        var row = await db.Queryable<FxArticleTranslation>()
            .Where(t => t.FxArticleId == guid && t.Locale == "en").FirstAsync();
        return row.Facts;
    }

    public static TheoryData<string> StructuredValues => new()
    {
        "{\"a\":1,\"b\":[true,null]}",
        "[1,2,3]",
        "5",
        "true",
        "\"hello\"",
        "\"{\\\"a\\\":1}\"",
    };

    [Theory]
    [MemberData(nameof(StructuredValues))]
    public async Task Translatable_json_reads_back_exactly_like_root_json(string raw)
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, raw);

        var item = await GetAsync(c, id);

        var facts = Facts(item);
        var attributes = item.GetProperty("attributes");
        facts.ValueKind.Should().Be(attributes.ValueKind);
        facts.GetRawText().Should().Be(attributes.GetRawText());
        facts.GetRawText().Should().Be(raw);
        (await StoredFactsAsync(id)).Should().Be(raw);
    }

    [Fact]
    public async Task Translatable_json_null_clears_the_value()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, "{\"a\":1}");

        var put = await c.PutAsync($"/api/items/{Collection}/{id}", Json(
            "{\"translations\":{\"en\":{\"facts\":null}}}"));

        put.StatusCode.Should().Be(HttpStatusCode.OK);
        Facts(await GetAsync(c, id)).ValueKind.Should().Be(JsonValueKind.Null);
        (await StoredFactsAsync(id)).Should().BeNull();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{bad")]
    [InlineData("")]
    public async Task Stored_text_that_is_not_json_reads_back_as_the_raw_string(string stored)
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, "{\"a\":1}");
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            var guid = Guid.Parse(id);
            await db.Updateable<FxArticleTranslation>()
                .SetColumns(t => t.Facts == stored)
                .Where(t => t.FxArticleId == guid && t.Locale == "en")
                .ExecuteCommandAsync();
        }

        var facts = Facts(await GetAsync(c, id));

        facts.ValueKind.Should().Be(JsonValueKind.String);
        facts.GetString().Should().Be(stored);
    }

    [Fact]
    public async Task Graphql_translation_json_has_the_shape_of_root_json()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, "{\"a\":1,\"b\":[true]}");

        var resp = await c.PostAsJsonAsync("/graphql", new
        {
            query = $"{{ fxArticle(id: \"{id}\") {{ attributes translations {{ locale fields }} }} }}",
        });

        var data = Root(await resp.Content.ReadAsStringAsync()).GetProperty("data").GetProperty("fxArticle");
        var en = data.GetProperty("translations").EnumerateArray()
            .Single(t => t.GetProperty("locale").GetString() == "en");
        var facts = en.GetProperty("fields").GetProperty("facts");
        facts.ValueKind.Should().Be(JsonValueKind.Object);
        facts.GetRawText().Should().Be(data.GetProperty("attributes").GetRawText());
        facts.GetProperty("b")[0].GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Graphql_mutation_stores_a_translation_json_string_as_a_json_string()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();

        var resp = await c.PostAsJsonAsync("/graphql", new
        {
            query = "mutation { createFxArticle(input: { status: \"draft\", translations: [ " +
                    "{ locale: \"en\", fields: { title: \"a\", facts: \"hello\" } } ] }) { id } }",
        });

        var root = Root(await resp.Content.ReadAsStringAsync());
        root.TryGetProperty("errors", out var errors).Should().BeFalse($"unexpected errors: {errors}");
        var id = root.GetProperty("data").GetProperty("createFxArticle").GetProperty("id").GetString()!;
        (await StoredFactsAsync(id)).Should().Be("\"hello\"");
        Facts(await GetAsync(c, id)).GetString().Should().Be("hello");
    }

    [Fact]
    public async Task Revision_snapshot_carries_the_structured_translation_value()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, "{\"a\":1}");

        var rev = Root(await c.GetStringAsync($"/api/items/{Collection}/{id}/revisions/1"))
            .GetProperty("data").GetProperty("snapshot");

        var facts = rev.GetProperty("translations").GetProperty("en").GetProperty("facts");
        facts.ValueKind.Should().Be(JsonValueKind.Object);
        facts.GetProperty("a").GetInt32().Should().Be(1);
        facts.GetRawText().Should().Be(rev.GetProperty("attributes").GetRawText());
    }

    [Fact]
    public async Task Stored_snapshot_carries_the_format_marker_and_rest_does_not_expose_it()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, "{\"a\":1}");

        var raw = await c.GetStringAsync($"/api/items/{Collection}/{id}/revisions/1");

        (await RawSnapshotKeys(id, 1)).Should().Contain(Marker);
        raw.Should().NotContain(Marker);
    }

    [Fact]
    public async Task Graphql_revision_response_does_not_expose_the_snapshot_format_marker()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, "{\"a\":1}");

        var resp = await c.PostAsJsonAsync("/graphql", new
        {
            query = $"{{ fxArticleRevision(id: \"{id}\", revisionNumber: 1) {{ snapshot }} }}",
        });

        var raw = await resp.Content.ReadAsStringAsync();
        raw.Should().Contain("translations").And.NotContain(Marker);
    }

    private async Task<List<string>> RawSnapshotKeys(string id, long number)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IRevisionStore>();
        var rec = await store.GetAsync(Collection, id, number);
        using var doc = JsonDocument.Parse(rec!.Snapshot);
        return doc.RootElement.EnumerateObject().Select(p => p.Name).ToList();
    }

    [Fact]
    public async Task Revert_of_a_new_snapshot_round_trips_structured_values_and_deliberate_strings()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, "{\"a\":1}");                                   // rev 1: structured
        (await c.PutAsync($"/api/items/{Collection}/{id}", Json(
            "{\"translations\":{\"en\":{\"facts\":\"{\\\"a\\\":1}\"}}}"))).EnsureSuccessStatusCode(); // rev 2: a string

        (await c.PostAsync($"/api/items/{Collection}/{id}/revisions/1/revert", null)).EnsureSuccessStatusCode();
        (await StoredFactsAsync(id)).Should().Be("{\"a\":1}");
        Facts(await GetAsync(c, id)).ValueKind.Should().Be(JsonValueKind.Object);

        (await c.PostAsync($"/api/items/{Collection}/{id}/revisions/2/revert", null)).EnsureSuccessStatusCode();
        // The snapshot re-encodes the string, so the stored text is an equivalent JSON string literal.
        JsonDocument.Parse((await StoredFactsAsync(id))!).RootElement.GetString().Should().Be("{\"a\":1}");
        var facts = Facts(await GetAsync(c, id));
        facts.ValueKind.Should().Be(JsonValueKind.String);
        facts.GetString().Should().Be("{\"a\":1}");
    }

    public static TheoryData<string> LegacyStoredTexts => new()
    {
        "{\"a\":1}",       // an object-looking string
        "plain text",      // not JSON at all
        "42",              // a numeric string
        " {\"a\": 1} ",    // JSON with surrounding whitespace
    };

    // A snapshot taken before the format marker holds a translatable Json value as the stored text.
    [Theory]
    [MemberData(nameof(LegacyStoredTexts))]
    public async Task Revert_of_a_snapshot_without_the_marker_restores_the_identical_stored_text(string legacyText)
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, "[9]");
        var legacyRevision = await CaptureLegacySnapshotAsync(id, legacyText);

        var revert = await c.PostAsync($"/api/items/{Collection}/{id}/revisions/{legacyRevision}/revert", null);

        revert.StatusCode.Should().Be(HttpStatusCode.OK, await revert.Content.ReadAsStringAsync());
        (await StoredFactsAsync(id)).Should().Be(legacyText);
    }

    [Fact]
    public async Task Revert_of_a_snapshot_without_the_marker_keeps_non_string_values_on_the_normal_rule()
    {
        var c = await _factory.CreateAuthenticatedClientAsync();
        var id = await CreateAsync(c, "[9]");
        var legacyRevision = await CaptureLegacySnapshotAsync(id, "ignored", factsRaw: "{\"a\":1}");

        (await c.PostAsync($"/api/items/{Collection}/{id}/revisions/{legacyRevision}/revert", null))
            .EnsureSuccessStatusCode();

        (await StoredFactsAsync(id)).Should().Be("{\"a\":1}");
    }

    // Builds an old-shape snapshot from the item's newest snapshot: the marker is removed and the
    // translatable Json value is the stored text (a JSON string), as snapshots were written before.
    private async Task<long> CaptureLegacySnapshotAsync(string id, string legacyText, string? factsRaw = null)
    {
        using var scope = _factory.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IRevisionStore>();
        var latest = (await store.ListAsync(Collection, id))[0].RevisionNumber;
        var rec = await store.GetAsync(Collection, id, latest);
        using var src = JsonDocument.Parse(rec!.Snapshot);

        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            foreach (var p in src.RootElement.EnumerateObject())
            {
                if (p.Name == Marker) continue;
                if (p.Name != "translations") { p.WriteTo(w); continue; }
                w.WriteStartObject("translations");
                foreach (var loc in p.Value.EnumerateObject())
                {
                    w.WriteStartObject(loc.Name);
                    foreach (var f in loc.Value.EnumerateObject())
                    {
                        if (f.Name != "facts") { f.WriteTo(w); continue; }
                        if (factsRaw is null) w.WriteString("facts", legacyText);
                        else { w.WritePropertyName("facts"); w.WriteRawValue(factsRaw); }
                    }
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            w.WriteEndObject();
        }
        await store.CaptureAsync(Collection, id, "update", Encoding.UTF8.GetString(ms.ToArray()));
        return (await store.ListAsync(Collection, id))[0].RevisionNumber;
    }
}
