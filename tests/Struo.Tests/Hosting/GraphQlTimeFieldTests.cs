using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Struo.Tests.Hosting;

/// <summary>
/// A <c>Time</c> field is a String in the GraphQL schema and holds a DateTime. Wherever it leaves GraphQL it
/// is the UTC instant ending in Z, whatever time zone the host is in; a value written with an offset, or
/// without one, is stored as the instant REST would store.
/// </summary>
[Collection("ApiIntegration")]
public sealed class GraphQlTimeFieldTests(ITestOutputHelper output)
{
    private const string Instant = "2026-03-04T05:06:07Z";

    private static readonly EmittedChild Row = new(
        "TimeRow", [new EmittedProperty("At", typeof(DateTime?), FieldInterface.Time)]);

    private static readonly EmittedEntity Entity = new(
        "TimeProbe",
        [
            new EmittedProperty("Name", typeof(string), FieldInterface.Text),
            new EmittedProperty("When", typeof(DateTime?), FieldInterface.Time),
            new EmittedProperty("Rows", null, FieldInterface.Repeater, false, Row),
        ],
        [new EmittedProperty("Clock", typeof(DateTime?), FieldInterface.Time)]);

    private static async Task<(ForkHost Host, HttpClient Client)> StartAsync()
    {
        var assembly = EmittedEntities.Build(EmittedEntities.UniqueName("timefield"), [Entity]);
        var host = await ForkHost.StartAsync(
            mutate: s =>
            {
                s["Struo:ContentAssemblies:1"] = assembly.GetName().Name;
                s["Auth:Cookie:RequireSecure"] = "false";
                s["RateLimiting:Login:Enabled"] = "false";
                s["GraphQl:ExposeSchema"] = "true";
            },
            environment: "Production", onSelectedBackend: true);
        var registry = host.App.Services.GetRequiredService<IEntityRegistry>();
        var sidecar = host.App.Services.GetRequiredService<IMetadataProvider>().GetCollection("timeProbe")!.Translation!.TranslationEntityType;
        using (var scope = host.App.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
            db.CodeFirst.InitTables(registry.Get("timeProbe")!.EntityType);
            db.CodeFirst.InitTables(sidecar);
        }
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Struo-CSRF", "1");
        var login = await client.PostAsync(
            "/api/auth/login",
            new StringContent($"{{\"email\":\"admin@admin.com\",\"password\":\"{TestBootstrapAdmin.Password}\"}}", Encoding.UTF8, "application/json"));
        login.StatusCode.Should().Be(HttpStatusCode.OK);
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0])));
        return (host, client);
    }

    private static async Task<JsonNode> GraphQlAsync(HttpClient client, string query, JsonObject? variables = null)
    {
        var request = new JsonObject { ["query"] = query };
        if (variables is not null) request["variables"] = variables;
        var response = await client.PostAsync("/graphql", new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"));
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        json["errors"].Should().BeNull(json.ToJsonString());
        return json;
    }

    private static async Task<string> CreateAsync(HttpClient client, string when, string at, string clock)
    {
        var input = new JsonObject
        {
            ["name"] = "t",
            ["when"] = when,
            ["rows"] = new JsonArray(new JsonObject { ["at"] = at }),
            ["translations"] = new JsonArray(new JsonObject
            {
                ["locale"] = "en", ["fields"] = new JsonObject { ["clock"] = clock },
            }),
        };
        var created = await GraphQlAsync(
            client, "mutation($input: TimeProbeCreateInput!) { createTimeProbe(input: $input) { id } }",
            new JsonObject { ["input"] = input });
        return created["data"]!["createTimeProbe"]!["id"]!.GetValue<string>();
    }

    [SqliteOnlyFact("starts a host on a SQLite temp database")]
    public async Task A_Time_value_written_with_or_without_an_offset_is_stored_as_the_instant_REST_stores()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        var withOffset = await CreateAsync(client, "2026-03-04T07:06:07+02:00", "2026-03-04T07:06:07+02:00", "2026-03-04T07:06:07+02:00");
        var withoutOffset = await CreateAsync(client, "2026-03-04T05:06:07", "2026-03-04T05:06:07", "2026-03-04T05:06:07");
        var viaRest = await client.PostAsync(
            "/api/items/timeProbe",
            new StringContent($"{{\"name\":\"r\",\"when\":\"{Instant}\",\"rows\":[],\"translations\":{{\"en\":{{\"clock\":\"{Instant}\"}}}}}}", Encoding.UTF8, "application/json"));
        var restText = await viaRest.Content.ReadAsStringAsync();
        viaRest.StatusCode.Should().Be(HttpStatusCode.Created, restText);
        var restId = JsonNode.Parse(restText)!["data"]!["id"]!.GetValue<string>();

        foreach (var id in new[] { withOffset, withoutOffset, restId })
        {
            var read = await client.GetAsync($"/api/items/timeProbe/{id}");
            JsonNode.Parse(await read.Content.ReadAsStringAsync())!["data"]!["when"]!.GetValue<string>().Should().Be(Instant);
        }
    }

    [SqliteOnlyFact("starts a host on a SQLite temp database")]
    public async Task A_Time_value_read_through_GraphQL_is_the_UTC_instant_ending_in_Z_everywhere_it_leaves()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        var id = await CreateAsync(client, "2026-03-04T07:06:07+02:00", "2026-03-04T05:06:07", Instant);

        var json = await GraphQlAsync(client, $$"""
            { timeProbe(id: "{{id}}") { when rows { at } translations { locale fields } } }
            """);
        var row = json["data"]!["timeProbe"]!;
        output.WriteLine(json.ToJsonString());

        row["when"]!.GetValue<string>().Should().Be(Instant);
        row["rows"]![0]!["at"]!.GetValue<string>().Should().Be(Instant);
        row["translations"]![0]!["fields"]!["clock"]!.GetValue<string>().Should().Be(Instant);
    }

    [SqliteOnlyFact("starts a host on a SQLite temp database")]
    public async Task A_Time_facet_prints_the_instant_in_UTC()
    {
        var (host, client) = await StartAsync();
        await using var _ = host;
        await CreateAsync(client, Instant, Instant, Instant);

        var json = await GraphQlAsync(client, """
            { timeProbes(facets: ["when"]) { facets { field values { value count } } } }
            """);
        output.WriteLine(json.ToJsonString());

        var list = json["data"]!["timeProbes"]!;
        list["facets"]![0]!["values"]![0]!["value"]!.ToJsonString().Should().Contain("2026-03-04T05:06:07").And.Contain("Z\"");
    }
}
