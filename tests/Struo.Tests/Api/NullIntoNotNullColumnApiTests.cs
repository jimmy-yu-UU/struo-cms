using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Infrastructure.Revisions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

/// <summary>
/// A write that leaves a NOT NULL column null answers 400 <c>BAD_USER_INPUT</c> before the database is
/// reached: the parent fields, the structured columns, the translation fields and the junction payload
/// fields, on create, update and revert. A column that accepts null, and a field that starts with a value,
/// keep their results.
/// </summary>
[Collection("ApiIntegration")]
public sealed class NullIntoNotNullColumnApiTests(ApiFactory factory)
{
    private const string Probes = "/api/items/fxNullProbe";
    private const string Shelves = "/api/items/fxNullShelf";
    private const string Heading = "\"translations\":{\"en\":{\"heading\":\"h\"}}";
    private const string Aliases = "\"translations\":{\"en\":{\"aliases\":[]}}";

    private static JsonElement Root(string body) => JsonDocument.Parse(body).RootElement;

    private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

    private static async Task<string> IdOfAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, text);
        return Root(text).GetProperty("data").GetProperty("id").GetString()!;
    }

    private static async Task<string> CreateProbeAsync(HttpClient client, string tail = Heading) =>
        await IdOfAsync(await client.PostAsync(Probes, Json("{\"plain\":\"x\"," + tail + "}")));

    private static async Task<string> CreateShelfAsync(HttpClient client) =>
        await IdOfAsync(await client.PostAsync(Shelves, Json("{\"name\":\"n\",\"labels\":[]," + Aliases + "}")));

    private static async Task AssertRefusedAsync(HttpResponseMessage response, string message)
    {
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest, text);
        var error = Root(text).GetProperty("error");
        error.GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        error.GetProperty("message").GetString().Should().Be(message);
    }

    private static async Task AssertTranslationRefusedAsync(HttpResponseMessage response, string field)
    {
        var message = $"Required translation field '{field}' is missing for locale 'en'.";
        await AssertRefusedAsync(response, message);
        var detail = Root(await response.Content.ReadAsStringAsync())
            .GetProperty("error").GetProperty("details").EnumerateArray().Single();
        detail.GetProperty("field").GetString().Should().Be(field);
        detail.GetProperty("locale").GetString().Should().Be("en");
        detail.GetProperty("message").GetString().Should().Be(message);
    }

    private static async Task<JsonElement> ReadAsync(HttpClient client, string url)
    {
        var text = await client.GetStringAsync(url);
        return Root(text).GetProperty("data").Clone();
    }

    [Fact]
    public async Task Create_without_a_NOT_NULL_field_that_has_no_initial_value_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        await AssertRefusedAsync(await client.PostAsync(Probes, Json("{" + Heading + "}")), "Field 'plain' is required.");
    }

    [Fact]
    public async Task Create_with_null_for_a_NOT_NULL_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        await AssertRefusedAsync(
            await client.PostAsync(Probes, Json("{\"plain\":null," + Heading + "}")), "Field 'plain' is required.");
    }

    [Fact]
    public async Task Create_with_null_for_a_NOT_NULL_field_that_has_an_initial_value_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        await AssertRefusedAsync(
            await client.PostAsync(Probes, Json("{\"plain\":\"x\",\"defaulted\":null," + Heading + "}")),
            "Field 'defaulted' is required.");
    }

    [Fact]
    public async Task Update_with_null_for_a_NOT_NULL_field_is_refused_and_the_row_keeps_its_value()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateProbeAsync(client);

        await AssertRefusedAsync(await client.PutAsync($"{Probes}/{id}", Json("{\"plain\":null}")), "Field 'plain' is required.");

        (await ReadAsync(client, $"{Probes}/{id}")).GetProperty("plain").GetString().Should().Be("x");
    }

    [Fact]
    public async Task Update_that_omits_a_NOT_NULL_field_keeps_the_stored_value()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateProbeAsync(client);

        var update = await client.PutAsync($"{Probes}/{id}", Json("{\"optional\":\"o\"}"));

        update.StatusCode.Should().Be(HttpStatusCode.OK);
        var data = await ReadAsync(client, $"{Probes}/{id}");
        data.GetProperty("plain").GetString().Should().Be("x");
        data.GetProperty("optional").GetString().Should().Be("o");
    }

    [Fact]
    public async Task Create_that_omits_a_NOT_NULL_field_with_an_initial_value_stores_the_value()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateProbeAsync(client);

        (await ReadAsync(client, $"{Probes}/{id}")).GetProperty("defaulted").GetString().Should().BeEmpty();
    }

    [Fact]
    public async Task A_blank_string_for_a_NOT_NULL_field_without_Required_is_stored()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var id = await IdOfAsync(await client.PostAsync(Probes, Json("{\"plain\":\" \"," + Heading + "}")));

        (await ReadAsync(client, $"{Probes}/{id}")).GetProperty("plain").GetString().Should().Be(" ");
    }

    [Fact]
    public async Task A_nullable_string_takes_null_or_no_value()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var withNull = await IdOfAsync(
            await client.PostAsync(Probes, Json("{\"plain\":\"x\",\"optional\":null," + Heading + "}")));
        var omitted = await CreateProbeAsync(client);

        (await ReadAsync(client, $"{Probes}/{withNull}")).GetProperty("optional").ValueKind.Should().Be(JsonValueKind.Null);
        (await ReadAsync(client, $"{Probes}/{omitted}")).GetProperty("optional").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task A_string_declared_nullable_takes_null_or_no_value()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var withNull = await IdOfAsync(
            await client.PostAsync(Probes, Json("{\"plain\":\"x\",\"declared\":null," + Heading + "}")));
        var omitted = await CreateProbeAsync(client);
        var update = await client.PutAsync($"{Probes}/{omitted}", Json("{\"declared\":null}"));

        update.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(client, $"{Probes}/{withNull}")).GetProperty("declared").ValueKind.Should().Be(JsonValueKind.Null);
        (await ReadAsync(client, $"{Probes}/{omitted}")).GetProperty("declared").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Create_with_null_for_a_structured_NOT_NULL_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        await AssertRefusedAsync(
            await client.PostAsync(Shelves, Json("{\"name\":\"n\",\"labels\":null," + Aliases + "}")),
            "Field 'labels' is required.");
    }

    [Fact]
    public async Task Create_without_a_structured_NOT_NULL_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        await AssertRefusedAsync(
            await client.PostAsync(Shelves, Json("{\"name\":\"n\"," + Aliases + "}")), "Field 'labels' is required.");
    }

    [Fact]
    public async Task Update_with_null_for_a_structured_NOT_NULL_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateShelfAsync(client);

        await AssertRefusedAsync(
            await client.PutAsync($"{Shelves}/{id}", Json("{\"labels\":null}")), "Field 'labels' is required.");
    }

    [Fact]
    public async Task An_empty_list_for_a_structured_NOT_NULL_field_is_stored()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateShelfAsync(client);

        (await ReadAsync(client, $"{Shelves}/{id}")).GetProperty("labels").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Create_with_null_for_a_NOT_NULL_translation_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        await AssertTranslationRefusedAsync(
            await client.PostAsync(Probes, Json("{\"plain\":\"x\",\"translations\":{\"en\":{\"heading\":null}}}")),
            "heading");
    }

    [Fact]
    public async Task Create_without_a_NOT_NULL_translation_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        await AssertTranslationRefusedAsync(
            await client.PostAsync(Probes, Json("{\"plain\":\"x\",\"translations\":{\"en\":{}}}")), "heading");
    }

    [Fact]
    public async Task Update_with_null_for_a_NOT_NULL_translation_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateProbeAsync(client);

        await AssertTranslationRefusedAsync(
            await client.PutAsync($"{Probes}/{id}", Json("{\"translations\":{\"en\":{\"heading\":null}}}")), "heading");
    }

    [Fact]
    public async Task Update_that_omits_a_NOT_NULL_translation_field_of_a_stored_locale_keeps_the_value()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateProbeAsync(client);

        var update = await client.PutAsync($"{Probes}/{id}", Json("{\"translations\":{\"en\":{}}}"));

        update.StatusCode.Should().Be(HttpStatusCode.OK);
        (await ReadAsync(client, $"{Probes}/{id}")).GetProperty("translations").GetProperty("en")
            .GetProperty("heading").GetString().Should().Be("h");
    }

    [Fact]
    public async Task Update_that_adds_a_locale_without_a_NOT_NULL_translation_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateProbeAsync(client);

        var response = await client.PutAsync($"{Probes}/{id}", Json("{\"translations\":{\"zh-TW\":{}}}"));

        await AssertRefusedAsync(response, "Required translation field 'heading' is missing for locale 'zh-TW'.");
    }

    [Fact]
    public async Task Create_with_null_for_a_structured_NOT_NULL_translation_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        await AssertTranslationRefusedAsync(
            await client.PostAsync(Shelves,
                Json("{\"name\":\"n\",\"labels\":[],\"translations\":{\"en\":{\"aliases\":null}}}")),
            "aliases");
    }

    [Fact]
    public async Task Create_without_a_structured_NOT_NULL_translation_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        await AssertTranslationRefusedAsync(
            await client.PostAsync(Shelves, Json("{\"name\":\"n\",\"labels\":[],\"translations\":{\"en\":{}}}")),
            "aliases");
    }

    [Fact]
    public async Task Update_with_null_for_a_structured_NOT_NULL_translation_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateShelfAsync(client);

        await AssertTranslationRefusedAsync(
            await client.PutAsync($"{Shelves}/{id}", Json("{\"translations\":{\"en\":{\"aliases\":null}}}")), "aliases");
    }

    [Fact]
    public async Task A_NOT_NULL_translation_field_with_an_initial_value_is_stored_when_omitted()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateShelfAsync(client);

        (await ReadAsync(client, $"{Shelves}/{id}")).GetProperty("translations").GetProperty("en")
            .GetProperty("heading").GetString().Should().BeEmpty();
    }

    private static string Link(string tagId, string payload = "") =>
        "{\"plain\":\"x\",\"tags\":[" + (payload.Length == 0 ? $"\"{tagId}\"" : "{\"id\":\"" + tagId + "\"," + payload + "}")
        + "]," + Heading + "}";

    private static async Task<string> CreateTagAsync(HttpClient client) =>
        await IdOfAsync(await client.PostAsync("/api/items/fxTag", Json("{\"name\":\"t\"}")));

    private static string LinkMessage(string tagId, string field) =>
        $"Relation 'tags', target '{tagId}': Field '{field}' is required.";

    [Fact]
    public async Task Create_with_a_bare_id_for_a_junction_that_has_a_NOT_NULL_payload_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client);

        await AssertRefusedAsync(await client.PostAsync(Probes, Json(Link(tag))), LinkMessage(tag, "remark"));
    }

    [Fact]
    public async Task Create_with_an_object_that_omits_a_NOT_NULL_payload_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client);

        await AssertRefusedAsync(
            await client.PostAsync(Probes, Json(Link(tag, "\"note\":\"n\""))), LinkMessage(tag, "remark"));
    }

    [Fact]
    public async Task Create_with_null_for_a_NOT_NULL_payload_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client);

        await AssertRefusedAsync(
            await client.PostAsync(Probes, Json(Link(tag, "\"remark\":null"))), LinkMessage(tag, "remark"));
    }

    [Fact]
    public async Task Create_with_null_for_a_NOT_NULL_payload_field_that_has_an_initial_value_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client);

        await AssertRefusedAsync(
            await client.PostAsync(Probes, Json(Link(tag, "\"remark\":\"r\",\"note\":null"))), LinkMessage(tag, "note"));
    }

    [Fact]
    public async Task Create_with_a_payload_that_fills_every_field_without_an_initial_value_is_stored()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client);

        var id = await IdOfAsync(await client.PostAsync(Probes, Json(Link(tag, "\"remark\":\"r\""))));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var probeId = Guid.Parse(id);
        var links = await db.Queryable<Struo.Tests.Fixtures.FxNullProbeTag>().Where(l => l.ProbeId == probeId).ToListAsync();
        var link = links.Should().ContainSingle().Subject;
        link.Remark.Should().Be("r");
        link.Note.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_that_adds_a_link_with_a_bare_id_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client);
        var id = await CreateProbeAsync(client);

        await AssertRefusedAsync(
            await client.PutAsync($"{Probes}/{id}", Json("{\"tags\":[\"" + tag + "\"]}")), LinkMessage(tag, "remark"));
    }

    [Fact]
    public async Task Update_that_keeps_a_link_with_a_bare_id_leaves_its_payload()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client);
        var id = await IdOfAsync(await client.PostAsync(Probes, Json(Link(tag, "\"remark\":\"r\""))));

        var update = await client.PutAsync($"{Probes}/{id}", Json("{\"tags\":[\"" + tag + "\"]}"));

        update.StatusCode.Should().Be(HttpStatusCode.OK, await update.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Update_with_null_for_the_NOT_NULL_payload_field_of_a_kept_link_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var tag = await CreateTagAsync(client);
        var id = await IdOfAsync(await client.PostAsync(Probes, Json(Link(tag, "\"remark\":\"r\""))));

        var update = await client.PutAsync(
            $"{Probes}/{id}", Json("{\"tags\":[{\"id\":\"" + tag + "\",\"remark\":null}]}"));

        await AssertRefusedAsync(update, LinkMessage(tag, "remark"));
    }

    [Fact]
    public async Task Revert_of_a_snapshot_that_holds_null_for_a_NOT_NULL_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateProbeAsync(client);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var latest = await db.Queryable<Revision>()
            .Where(r => r.CollectionName == "fxNullProbe" && r.ItemId == id)
            .OrderByDescending(r => r.RevisionNumber).FirstAsync();
        var snapshot = System.Text.Json.Nodes.JsonNode.Parse(latest.Snapshot)!.AsObject();
        snapshot["plain"] = null;
        var number = latest.RevisionNumber + 1;
        await db.Insertable(new Revision
        {
            Id = Guid.NewGuid(), CollectionName = "fxNullProbe", ItemId = id, RevisionNumber = number,
            Operation = "update", Snapshot = snapshot.ToJsonString(), CreatedAt = DateTime.UtcNow,
        }).ExecuteCommandAsync();

        var revert = await client.PostAsync($"{Probes}/{id}/revisions/{number}/revert", Json(""));

        await AssertRefusedAsync(revert, "Field 'plain' is required.");
        (await ReadAsync(client, $"{Probes}/{id}")).GetProperty("plain").GetString().Should().Be("x");
    }

    private static async Task<JsonElement> GraphQlAsync(HttpClient client, string query)
    {
        var body = new System.Text.Json.Nodes.JsonObject { ["query"] = query }.ToJsonString();
        var response = await client.PostAsync("/graphql", Json(body));
        return Root(await response.Content.ReadAsStringAsync());
    }

    private static void AssertGraphQlRefused(JsonElement root, string message)
    {
        root.TryGetProperty("errors", out var errors).Should().BeTrue(root.GetRawText());
        errors.GetArrayLength().Should().Be(1, root.GetRawText());
        errors[0].GetProperty("extensions").GetProperty("code").GetString().Should().Be("BAD_USER_INPUT");
        errors[0].GetProperty("message").GetString().Should().Be(message);
    }

    private const string GqlHeading = "translations: [{locale: \"en\", fields: {heading: \"h\"}}]";

    [Fact]
    public async Task GraphQl_create_with_null_for_a_NOT_NULL_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        AssertGraphQlRefused(
            await GraphQlAsync(client, "mutation { createFxNullProbe(input: {plain: null, " + GqlHeading + "}) { id } }"),
            "Field 'plain' is required.");
    }

    [Fact]
    public async Task GraphQl_create_without_a_NOT_NULL_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        AssertGraphQlRefused(
            await GraphQlAsync(client, "mutation { createFxNullProbe(input: {" + GqlHeading + "}) { id } }"),
            "Field 'plain' is required.");
    }

    [Fact]
    public async Task GraphQl_create_with_null_for_a_NOT_NULL_field_that_has_an_initial_value_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        AssertGraphQlRefused(
            await GraphQlAsync(client,
                "mutation { createFxNullProbe(input: {plain: \"x\", defaulted: null, " + GqlHeading + "}) { id } }"),
            "Field 'defaulted' is required.");
    }

    [Fact]
    public async Task GraphQl_update_with_null_for_a_NOT_NULL_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateProbeAsync(client);

        AssertGraphQlRefused(
            await GraphQlAsync(client, $"mutation {{ updateFxNullProbe(id: \"{id}\", input: {{plain: null}}) {{ id }} }}"),
            "Field 'plain' is required.");
    }

    [Fact]
    public async Task GraphQl_create_with_null_for_a_NOT_NULL_translation_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        AssertGraphQlRefused(
            await GraphQlAsync(client,
                "mutation { createFxNullProbe(input: {plain: \"x\", translations: [{locale: \"en\", fields: {heading: null}}]}) { id } }"),
            "Required translation field 'heading' is missing for locale 'en'.");
    }

    [Fact]
    public async Task GraphQl_update_with_null_for_a_NOT_NULL_translation_field_is_refused()
    {
        var client = await factory.CreateAuthenticatedClientAsync();
        var id = await CreateProbeAsync(client);

        AssertGraphQlRefused(
            await GraphQlAsync(client,
                $"mutation {{ updateFxNullProbe(id: \"{id}\", input: {{translations: [{{locale: \"en\", fields: {{heading: null}}}}]}}) {{ id }} }}"),
            "Required translation field 'heading' is missing for locale 'en'.");
    }

    [Fact]
    public async Task GraphQl_create_with_null_for_a_string_declared_nullable_is_stored()
    {
        var client = await factory.CreateAuthenticatedClientAsync();

        var root = await GraphQlAsync(client,
            "mutation { createFxNullProbe(input: {plain: \"x\", declared: null, " + GqlHeading + "}) { id declared } }");

        root.TryGetProperty("errors", out _).Should().BeFalse(root.GetRawText());
        root.GetProperty("data").GetProperty("createFxNullProbe").GetProperty("declared").ValueKind
            .Should().Be(JsonValueKind.Null);
    }
}
