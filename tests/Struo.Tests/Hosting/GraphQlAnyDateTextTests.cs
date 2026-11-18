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
/// A <c>Date</c>, <c>DateTime</c> or <c>Time</c> field that GraphQL prints inside an <c>Any</c> value (a
/// translation's fields, a facet value, an aggregate value, a revision snapshot) has the text the own field
/// prints: <c>Date</c> as the date, <c>DateTime</c> and <c>Time</c> as the UTC instant ending in Z, with
/// fractional seconds only when there are some. A null stays null. Runs on the backend
/// <c>STRUO_TEST_BACKEND</c> selects.
/// </summary>
[Collection("ApiIntegration")]
public sealed class GraphQlAnyDateTextTests(ITestOutputHelper output)
{
    private const string FractionalInstant = "2026-03-04T05:06:07.25Z";
    private const string WholeInstant = "2026-03-05T08:09:10Z";
    private const string FractionalDay = "2026-03-04";
    private const string WholeDay = "2026-03-05";

    private static readonly EmittedChild Row = new(
        "DateRow",
        [
            new EmittedProperty("Day", typeof(DateTime?), FieldInterface.Date),
            new EmittedProperty("Moment", typeof(DateTime?), FieldInterface.DateTime),
            new EmittedProperty("Clock", typeof(DateTime?), FieldInterface.Time),
        ]);

    private static readonly EmittedEntity Entity = new(
        "DateProbe",
        [
            new EmittedProperty("Name", typeof(string), FieldInterface.Text),
            new EmittedProperty("Day", typeof(DateTime?), FieldInterface.Date),
            new EmittedProperty("Moment", typeof(DateTime?), FieldInterface.DateTime),
            new EmittedProperty("Clock", typeof(DateTime?), FieldInterface.Time),
            new EmittedProperty("Rows", null, FieldInterface.Repeater, false, Row),
        ],
        [
            new EmittedProperty("LocaleDay", typeof(DateTime?), FieldInterface.Date),
            new EmittedProperty("LocaleMoment", typeof(DateTime?), FieldInterface.DateTime),
            new EmittedProperty("LocaleClock", typeof(DateTime?), FieldInterface.Time),
        ],
        Revisions: true);

    private sealed record Probe(HttpClient Client, string FractionalId, string WholeId, string NullId);

    private static async Task<(ForkHost Host, Probe Probe)> StartAsync()
    {
        var assembly = EmittedEntities.Build(EmittedEntities.UniqueName("anydate"), [Entity]);
        var host = await ForkHost.StartAsync(
            mutate: s =>
            {
                s["Struo:ContentAssemblies:1"] = assembly.GetName().Name;
                s["Auth:Cookie:RequireSecure"] = "false";
                s["RateLimiting:Login:Enabled"] = "false";
                s["GraphQl:ExposeSchema"] = "true";
            },
            environment: "Production", onSelectedBackend: true);
        try
        {
            CreateTables(host);
            var client = await LoginAsync(host);
            var probe = new Probe(
                client,
                await CreateAsync(client, FractionalDay, FractionalInstant),
                await CreateAsync(client, WholeDay, WholeInstant),
                await CreateAsync(client, null, null));
            return (host, probe);
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private static void CreateTables(ForkHost host)
    {
        var registry = host.App.Services.GetRequiredService<IEntityRegistry>();
        var sidecar = host.App.Services.GetRequiredService<IMetadataProvider>().GetCollection("dateProbe")!.Translation!.TranslationEntityType;
        using var scope = host.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        db.CodeFirst.InitTables(registry.Get("dateProbe")!.EntityType);
        db.CodeFirst.InitTables(sidecar);
    }

    private static async Task<HttpClient> LoginAsync(ForkHost host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Struo-CSRF", "1");
        var login = await client.PostAsync(
            "/api/auth/login",
            new StringContent($"{{\"email\":\"admin@admin.com\",\"password\":\"{TestBootstrapAdmin.Password}\"}}", Encoding.UTF8, "application/json"));
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        client.DefaultRequestHeaders.Add("Cookie", string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0])));
        return client;
    }

    // Written through REST, which stores the value as sent and is not under test.
    private static async Task<string> CreateAsync(HttpClient client, string? day, string? instant)
    {
        var body = new JsonObject
        {
            ["name"] = day ?? "null",
            ["day"] = day,
            ["moment"] = instant,
            ["clock"] = instant,
            ["rows"] = new JsonArray(new JsonObject { ["day"] = day, ["moment"] = instant, ["clock"] = instant }),
            ["translations"] = new JsonObject
            {
                ["en"] = new JsonObject { ["localeDay"] = day, ["localeMoment"] = instant, ["localeClock"] = instant },
            },
        };
        var response = await client.PostAsync("/api/items/dateProbe", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, text);
        return JsonNode.Parse(text)!["data"]!["id"]!.GetValue<string>();
    }

    private async Task<JsonNode> QueryAsync(HttpClient client, string query)
    {
        var request = new JsonObject { ["query"] = query };
        var response = await client.PostAsync("/graphql", new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"));
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        output.WriteLine(json.ToJsonString());
        json["errors"].Should().BeNull(json.ToJsonString());
        return json;
    }

    private static string Text(JsonNode? node) => node is null ? "(null)" : node.GetValue<string>();

    private async Task<JsonNode> ItemAsync(Probe p, string id, string selection) =>
        (await QueryAsync(p.Client, $$"""{ dateProbe(id: "{{id}}") { {{selection}} } }"""))["data"]!["dateProbe"]!;

    [Fact]
    public async Task The_own_fields_print_the_text_the_Any_positions_are_compared_with()
    {
        var (host, p) = await StartAsync();
        await using var _ = host;

        var fractional = await ItemAsync(p, p.FractionalId, "day moment clock rows { day moment clock }");
        var whole = await ItemAsync(p, p.WholeId, "day moment clock rows { day moment clock }");
        var none = await ItemAsync(p, p.NullId, "day moment clock rows { day moment clock }");

        Text(fractional["day"]).Should().Be(FractionalDay);
        Text(fractional["moment"]).Should().Be(FractionalInstant);
        Text(fractional["clock"]).Should().Be(FractionalInstant);
        Text(whole["day"]).Should().Be(WholeDay);
        Text(whole["moment"]).Should().Be(WholeInstant);
        Text(whole["clock"]).Should().Be(WholeInstant);
        none["day"].Should().BeNull();
        none["moment"].Should().BeNull();
        none["clock"].Should().BeNull();
        Text(fractional["rows"]![0]!["day"]).Should().Be(FractionalDay);
        Text(fractional["rows"]![0]!["moment"]).Should().Be(FractionalInstant);
        Text(fractional["rows"]![0]!["clock"]).Should().Be(FractionalInstant);
    }

    [Fact]
    public async Task A_translation_prints_Date_DateTime_and_Time_as_the_own_fields_do()
    {
        var (host, p) = await StartAsync();
        await using var _ = host;

        const string Selection = "day moment clock translations { locale fields }";
        var fractional = await ItemAsync(p, p.FractionalId, Selection);
        var whole = await ItemAsync(p, p.WholeId, Selection);
        var none = await ItemAsync(p, p.NullId, Selection);

        var fields = fractional["translations"]![0]!["fields"]!;
        Text(fields["localeDay"]).Should().Be(FractionalDay).And.Be(Text(fractional["day"]));
        Text(fields["localeMoment"]).Should().Be(FractionalInstant).And.Be(Text(fractional["moment"]));
        Text(fields["localeClock"]).Should().Be(FractionalInstant).And.Be(Text(fractional["clock"]));
        fields = whole["translations"]![0]!["fields"]!;
        Text(fields["localeDay"]).Should().Be(WholeDay).And.Be(Text(whole["day"]));
        Text(fields["localeMoment"]).Should().Be(WholeInstant).And.Be(Text(whole["moment"]));
        Text(fields["localeClock"]).Should().Be(WholeInstant).And.Be(Text(whole["clock"]));
        fields = none["translations"]![0]!["fields"]!;
        fields["localeDay"].Should().BeNull();
        fields["localeMoment"].Should().BeNull();
        fields["localeClock"].Should().BeNull();
    }

    [Fact]
    public async Task A_facet_value_prints_Date_DateTime_and_Time_as_the_own_fields_do()
    {
        var (host, p) = await StartAsync();
        await using var _ = host;

        var json = await QueryAsync(p.Client, """
            { dateProbes(facets: ["day", "moment", "clock"]) { facets { field values { value count } } } }
            """);

        var facets = json["data"]!["dateProbes"]!["facets"]!.AsArray();
        Values(facets, "day").Should().BeEquivalentTo(FractionalDay, WholeDay);
        Values(facets, "moment").Should().BeEquivalentTo(FractionalInstant, WholeInstant);
        Values(facets, "clock").Should().BeEquivalentTo(FractionalInstant, WholeInstant);
    }

    // The text of each non-null bucket of one field.
    private static List<string> Values(JsonArray facets, string field) =>
        facets.Single(f => f!["field"]!.GetValue<string>() == field)!["values"]!.AsArray()
            .Where(v => v!["value"] is not null).Select(v => v!["value"]!.GetValue<string>()).ToList();

    [Fact]
    public async Task An_aggregate_min_and_max_print_Date_and_DateTime_as_the_own_fields_do()
    {
        var (host, p) = await StartAsync();
        await using var _ = host;

        var json = await QueryAsync(p.Client, """
            { dateProbes(aggregate: { min: ["day", "moment"], max: ["day", "moment"], count: ["moment"] }) { aggregate } }
            """);

        var aggregate = json["data"]!["dateProbes"]!["aggregate"]!;
        Text(aggregate["min"]!["day"]).Should().Be(FractionalDay);
        Text(aggregate["min"]!["moment"]).Should().Be(FractionalInstant);
        Text(aggregate["max"]!["day"]).Should().Be(WholeDay);
        Text(aggregate["max"]!["moment"]).Should().Be(WholeInstant);
        aggregate["count"]!["moment"]!.GetValue<long>().Should().Be(2);
    }

    [Fact]
    public async Task An_aggregate_over_rows_without_a_value_prints_null()
    {
        var (host, p) = await StartAsync();
        await using var _ = host;

        var json = await QueryAsync(p.Client, """
            { dateProbes(filter: { name: { eq: "null" } }, aggregate: { min: ["day", "moment"], max: ["day", "moment"] }) { aggregate } }
            """);

        var aggregate = json["data"]!["dateProbes"]!["aggregate"]!;
        foreach (var op in new[] { "min", "max" })
            foreach (var field in new[] { "day", "moment" })
                aggregate[op]![field].Should().BeNull($"{op} of {field}");
    }
}
