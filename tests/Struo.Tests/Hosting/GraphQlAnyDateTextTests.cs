using System.Text.Json.Nodes;
using AwesomeAssertions;
using Struo.Domain.Metadata.Enums;
using Struo.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Struo.Tests.Hosting;

/// <summary>
/// A <c>Date</c>, <c>DateTime</c> or <c>Time</c> field that GraphQL prints inside an <c>Any</c> value (a
/// translation's fields, a facet value, an aggregate value) has the text the own field prints: <c>Date</c> as
/// the date, <c>DateTime</c> and <c>Time</c> as the UTC instant ending in Z, with fractional seconds only when
/// there are some. A null is null. Runs on the backend <c>STRUO_TEST_BACKEND</c> selects.
/// </summary>
[Collection("ApiIntegration")]
public sealed class GraphQlAnyDateTextTests(ITestOutputHelper output)
{
    private const string FractionalInstant = "2026-03-04T05:06:07.25Z";
    private const string WholeInstant = "2026-03-05T08:09:10Z";
    private const string FractionalDay = "2026-03-04";
    private const string WholeDay = "2026-03-05";

    private static readonly string[] ValueOps = ["min", "max"];
    private static readonly string[] Aggregatable = ["day", "moment"];
    private static readonly string[] AllFields = ["day", "moment", "clock"];

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
        ]);

    // Two date fields whose names differ only in case: `stamp` (DateTime) and `sTAMP` (Date).
    private static readonly EmittedEntity CaseEntity = new(
        "CaseProbe",
        [
            new EmittedProperty("Name", typeof(string), FieldInterface.Text),
            new EmittedProperty("Stamp", typeof(DateTime?), FieldInterface.DateTime),
            new EmittedProperty("STAMP", typeof(DateTime?), FieldInterface.Date),
        ]);

    private sealed record Probe(HttpClient Client, string FractionalId, string WholeId, string NullId);

    private static async Task<(ForkHost Host, Probe Probe)> StartAsync()
    {
        var (host, client) = await EmittedGraphQlHost.StartAsync(Entity, "anydate");
        try
        {
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

    private static Task<string> CreateAsync(HttpClient client, string? day, string? instant) =>
        EmittedGraphQlHost.CreateAsync(client, "dateProbe", new JsonObject
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
        });

    private Task<JsonNode> QueryAsync(HttpClient client, string query) => EmittedGraphQlHost.QueryAsync(client, output, query);

    private static string Text(JsonNode? node) => node is null ? "(null)" : node.GetValue<string>();

    private async Task<JsonNode> ItemAsync(Probe p, string id, string selection) =>
        (await QueryAsync(p.Client, $$"""{ dateProbe(id: "{{id}}") { {{selection}} } }"""))["data"]!["dateProbe"]!;

    // The text of the non-null values of one own field over all items.
    private static List<string> Own(JsonNode list, string field) =>
        list["items"]!.AsArray().Where(i => i![field] is not null).Select(i => i![field]!.GetValue<string>()).ToList();

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
            { dateProbes(facets: ["day", "moment", "clock"]) { items { day moment clock } facets { field values { value count } } } }
            """);

        var list = json["data"]!["dateProbes"]!;
        var facets = list["facets"]!.AsArray();
        Values(facets, "day").Should().BeEquivalentTo(Own(list, "day")).And.BeEquivalentTo(FractionalDay, WholeDay);
        Values(facets, "moment").Should().BeEquivalentTo(Own(list, "moment")).And.BeEquivalentTo(FractionalInstant, WholeInstant);
        Values(facets, "clock").Should().BeEquivalentTo(Own(list, "clock")).And.BeEquivalentTo(FractionalInstant, WholeInstant);
        foreach (var field in AllFields)
            NullBuckets(facets, field).Should().Be(1, $"the row without a {field} is one null bucket");
    }

    // The text of each non-null bucket of one field.
    private static List<string> Values(JsonArray facets, string field) =>
        Buckets(facets, field).Where(v => v!["value"] is not null).Select(v => v!["value"]!.GetValue<string>()).ToList();

    // How many buckets of one field have the value null (not a string).
    private static int NullBuckets(JsonArray facets, string field) => Buckets(facets, field).Count(v => v!["value"] is null);

    private static JsonArray Buckets(JsonArray facets, string field) =>
        facets.Single(f => f!["field"]!.GetValue<string>() == field)!["values"]!.AsArray();

    [Fact]
    public async Task An_aggregate_min_and_max_print_Date_and_DateTime_as_the_own_fields_do()
    {
        var (host, p) = await StartAsync();
        await using var _ = host;

        var json = await QueryAsync(p.Client, """
            { dateProbes(aggregate: { min: ["day", "moment"], max: ["day", "moment"], count: ["moment"] }) {
                items { day moment } aggregate } }
            """);

        var list = json["data"]!["dateProbes"]!;
        var aggregate = list["aggregate"]!;
        Text(aggregate["min"]!["day"]).Should().Be(FractionalDay).And.Be(Own(list, "day").Min());
        Text(aggregate["min"]!["moment"]).Should().Be(FractionalInstant).And.Be(Own(list, "moment").Min());
        Text(aggregate["max"]!["day"]).Should().Be(WholeDay).And.Be(Own(list, "day").Max());
        Text(aggregate["max"]!["moment"]).Should().Be(WholeInstant).And.Be(Own(list, "moment").Max());
        aggregate["count"]!["moment"]!.GetValue<long>().Should().Be(2);
    }

    [Fact]
    public async Task A_facet_and_an_aggregate_named_in_another_case_print_the_same_text()
    {
        var (host, p) = await StartAsync();
        await using var _ = host;

        var json = await QueryAsync(p.Client, """
            { dateProbes(facets: ["Moment"], aggregate: { min: ["Day"], max: ["Moment"] }) {
                items { day moment } facets { field values { value } } aggregate } }
            """);

        var list = json["data"]!["dateProbes"]!;
        Values(list["facets"]!.AsArray(), "Moment").Should().BeEquivalentTo(Own(list, "moment"))
            .And.BeEquivalentTo(FractionalInstant, WholeInstant);
        Text(list["aggregate"]!["min"]!["Day"]).Should().Be(FractionalDay).And.Be(Own(list, "day").Min());
        Text(list["aggregate"]!["max"]!["Moment"]).Should().Be(WholeInstant).And.Be(Own(list, "moment").Max());
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
        foreach (var op in ValueOps)
            foreach (var field in Aggregatable)
                aggregate[op]![field].Should().BeNull($"{op} of {field}");
    }

    [Fact]
    public async Task Two_date_fields_named_in_different_case_start_and_a_facet_prints_the_first_one_like_the_field()
    {
        var (host, client) = await EmittedGraphQlHost.StartAsync(CaseEntity, "anydatecase");
        await using var _ = host;
        await EmittedGraphQlHost.CreateAsync(client, "caseProbe", new JsonObject
        {
            ["name"] = "c", ["stamp"] = FractionalInstant, ["sTAMP"] = WholeDay,
        });

        var json = await QueryAsync(client, """
            { caseProbes(facets: ["stamp", "sTAMP"]) { items { stamp sTAMP } facets { field values { value } } } }
            """);

        var list = json["data"]!["caseProbes"]!;
        Text(list["items"]![0]!["stamp"]).Should().Be(FractionalInstant);
        Text(list["items"]![0]!["sTAMP"]).Should().Be(WholeDay);
        var facets = list["facets"]!.AsArray();
        Values(facets, "stamp").Should().Equal(FractionalInstant);
        Values(facets, "sTAMP").Should().Equal(FractionalInstant, "a name that matches two fields ignoring case reads the first field");
    }
}
