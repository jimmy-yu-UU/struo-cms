using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Tests.Metadata;
using Struo.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Struo.Tests.Hosting;

/// <summary>
/// Every pair of interface and property type the scan accepts has to work: a record created through REST
/// reads back with the values it was sent, a scalar is found by <c>filter[..][_eq]</c>, and an interface
/// that GraphQL exposes also answers a query and a create mutation. A Repeater is covered with one
/// sub-field per interface and type it allows. Runs on the backend <c>STRUO_TEST_BACKEND</c> selects.
/// </summary>
[Collection("ApiIntegration")]
public sealed class AcceptedCellsRoundTripTests(ITestOutputHelper output)
{
    private const string Guid1 = "11111111-2222-3333-4444-555555555555";

    private static readonly FieldInterface[] RepeaterInterfaces =
    [
        FieldInterface.Text, FieldInterface.Textarea, FieldInterface.Markdown, FieldInterface.Code,
        FieldInterface.Slug, FieldInterface.Email, FieldInterface.Url, FieldInterface.Color, FieldInterface.Phone,
        FieldInterface.Number, FieldInterface.Slider, FieldInterface.Rating, FieldInterface.Boolean,
        FieldInterface.Checkbox, FieldInterface.Date, FieldInterface.Time, FieldInterface.DateTime,
        FieldInterface.Select, FieldInterface.Radio,
    ];

    // Interfaces whose scalar the list endpoint filters with _eq.
    private static readonly FieldInterface[] Filterable =
    [
        FieldInterface.Text, FieldInterface.Textarea, FieldInterface.Slug, FieldInterface.Email, FieldInterface.Url,
        FieldInterface.Color, FieldInterface.Phone, FieldInterface.Select, FieldInterface.Radio, FieldInterface.Number,
        FieldInterface.Slider, FieldInterface.Rating, FieldInterface.Boolean, FieldInterface.Checkbox,
        FieldInterface.Date, FieldInterface.Time, FieldInterface.DateTime, FieldInterface.Uuid,
    ];

    private static readonly EmittedChild PlainRow =
        new("PlainRow", [new EmittedProperty("Name", typeof(string), FieldInterface.Text)], Plain: true);

    private static string PropertyName(string id) =>
        "F" + id.Replace("?", "Nullable").Replace("[]", "Array").Replace("<", "").Replace(">", "").Replace(",", "");

    // The suffix keeps `rtFile` and `rtFiles` from generating the same GraphQL field name.
    private static string EntityName(FieldInterface face) => "Rt" + face + "Cell";

    private static string Camel(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);

    // Custom, List<Custom> and List<Plain> become the emitted plain row class.
    private static EmittedProperty Property(string name, FieldInterface face, string id, Type type, bool options) =>
        id switch
        {
            "Custom" => new EmittedProperty(name, null, face, options, PlainRow, ChildIsList: false),
            "List<Custom>" or "List<Plain>" => new EmittedProperty(name, null, face, options, PlainRow),
            _ => new EmittedProperty(name, type, face, options),
        };

    private static string SampleJson(FieldInterface face, string id) => id.TrimEnd('?') switch
    {
        "string" => face switch
        {
            FieldInterface.Email => "\"a@example.com\"",
            FieldInterface.Url => "\"https://example.com/x\"",
            FieldInterface.Color => "\"#ff0000\"",
            FieldInterface.Slug => "\"hello-world\"",
            FieldInterface.Json => "{\"a\":1}",
            FieldInterface.Select or FieldInterface.Radio => "\"hello\"",
            _ => "\"hello\"",
        },
        "int" => "42",
        "long" => "4200000000",
        "decimal" => "12.5",
        "double" => "3.25",
        "float" => "1.5",
        "bool" => "true",
        "DateTime" => "\"2026-03-04T05:06:07Z\"",
        "Guid" => $"\"{Guid1}\"",
        "byte[]" => "\"AQID\"",
        "List<string>" or "string[]" => "[\"a\",\"b\"]",
        "List<int>" => "[1,2]",
        "List<Guid>" => $"[\"{Guid1}\"]",
        "List<TagItem>" => "[{\"value\":\"a\",\"label\":\"A\"}]",
        "Dictionary<string,string>" => "{\"k\":\"v\"}",
        "Dictionary<string,int>" => "{\"k\":1}",
        "Custom" => "{\"name\":\"n\"}",
        "List<Custom>" or "List<Plain>" => "[{\"name\":\"n\"}]",
        _ => throw new InvalidOperationException($"No sample for type '{id}'."),
    };

    private static string FilterText(FieldInterface face, string id) => id.TrimEnd('?') switch
    {
        "string" => face switch
        {
            FieldInterface.Email => "a@example.com",
            FieldInterface.Url => "https://example.com/x",
            FieldInterface.Color => "#ff0000",
            FieldInterface.Slug => "hello-world",
            _ => "hello",
        },
        "DateTime" => "2026-03-04T05:06:07Z",
        "Guid" => Guid1,
        var other => SampleJson(face, other),
    };

    private static bool IsScalar(string id) =>
        id.TrimEnd('?') is "string" or "int" or "long" or "decimal" or "double" or "float" or "bool" or "DateTime" or "Guid";

    private sealed record Cell(FieldInterface Face, string Id, string Name, bool Sub);

    private static IEnumerable<Cell> OwnCells(FieldInterface face) =>
        face == FieldInterface.Repeater ? [] : InterfaceTypeExpectations.Accepted[face].Select(id => new Cell(face, id, PropertyName(id), false));

    private static IEnumerable<Cell> SubCells() =>
        RepeaterInterfaces.SelectMany(face =>
            InterfaceTypeExpectations.Accepted[face].Select(id => new Cell(face, id, face + PropertyName(id), true)));

    private static EmittedEntity EntityFor(FieldInterface face)
    {
        var types = InterfaceTypeExpectations.Types.ToDictionary(t => t.Id, t => t.Type);
        var options = InterfaceTypeExpectations.WithOptions.Contains(face);
        var properties = new List<EmittedProperty>
        {
            new("Anchor", typeof(string), FieldInterface.Text),
        };
        if (face == FieldInterface.Repeater)
        {
            var row = new EmittedChild("RepeaterRow", SubCells()
                .Select(c => Property(c.Name, c.Face, c.Id, types[c.Id], RepeaterOptions(c.Face))).ToList());
            properties.Add(new EmittedProperty("Rows", null, FieldInterface.Repeater, false, row));
        }
        else
        {
            properties.AddRange(OwnCells(face).Select(c => Property(c.Name, face, c.Id, types[c.Id], options)));
        }
        return new EmittedEntity(EntityName(face), properties);
    }

    private static bool RepeaterOptions(FieldInterface face) => face is FieldInterface.Select or FieldInterface.Radio;

    private static JsonObject BodyFor(FieldInterface face)
    {
        var body = new JsonObject { ["anchor"] = "x" };
        if (face == FieldInterface.Repeater)
        {
            var row = new JsonObject();
            foreach (var c in SubCells()) row[Camel(c.Name)] = JsonNode.Parse(SampleJson(c.Face, c.Id));
            body["rows"] = new JsonArray(row);
            return body;
        }
        foreach (var c in OwnCells(face)) body[Camel(c.Name)] = JsonNode.Parse(SampleJson(face, c.Id));
        return body;
    }

    [Fact]
    public async Task Every_accepted_pair_creates_reads_back_and_filters()
    {
        var assembly = EmittedEntities.Build(
            EmittedEntities.UniqueName("roundtrip"),
            Enum.GetValues<FieldInterface>().Select(EntityFor).ToList());
        await using var host = await ForkHost.StartAsync(
            mutate: s =>
            {
                s["Struo:ContentAssemblies:1"] = assembly.GetName().Name;
                s["Auth:Cookie:RequireSecure"] = "false";
                s["RateLimiting:Login:Enabled"] = "false";
                s["RateLimiting:LoginAccount:PermitLimit"] = "100000";
                s["GraphQl:ExposeSchema"] = "true";
            },
            environment: "Production", onSelectedBackend: true);
        CreateTables(host, assembly);
        var client = await LoginAsync(host);

        var failures = new List<string>();
        var checkedCells = 0;
        var graphQlInterfaces = 0;
        foreach (var face in Enum.GetValues<FieldInterface>())
        {
            var collection = Camel(EntityName(face));
            var body = BodyFor(face);
            var id = await CreateAsync(client, collection, body, failures, face);
            if (id is null) continue;
            checkedCells += await VerifyAsync(client, collection, id, face, body, failures);
            await VerifyNullsAsync(client, collection, id, face, failures);
            graphQlInterfaces += await GraphQlAsync(client, face, collection, body, failures);
        }

        output.WriteLine(
            $"Backend: {TestBackend.Current}; cells checked: {checkedCells}; interfaces through GraphQL: {graphQlInterfaces}; failures: {failures.Count}");
        failures.Should().BeEmpty(string.Join(Environment.NewLine, failures));
        var expectedCells = Enum.GetValues<FieldInterface>().Sum(f => OwnCells(f).Count()) + SubCells().Count();
        checkedCells.Should().Be(expectedCells);
        graphQlInterfaces.Should().Be(Enum.GetValues<FieldInterface>().Count(f => SchemaExposed(f).Count > 0));
    }

    private static void CreateTables(ForkHost host, System.Reflection.Assembly assembly)
    {
        var registry = host.App.Services.GetRequiredService<IEntityRegistry>();
        using var scope = host.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        foreach (var face in Enum.GetValues<FieldInterface>())
            db.CodeFirst.InitTables(registry.Get(Camel(EntityName(face)))!.EntityType);
    }

    private static async Task<HttpClient> LoginAsync(ForkHost host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Struo-CSRF", "1");
        var login = await client.PostAsync(
            "/api/auth/login",
            new StringContent(
                $"{{\"email\":\"admin@admin.com\",\"password\":\"{TestBootstrapAdmin.Password}\"}}",
                Encoding.UTF8, "application/json"));
        login.StatusCode.Should().Be(HttpStatusCode.OK, await login.Content.ReadAsStringAsync());
        var cookie = string.Join("; ", login.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
        client.DefaultRequestHeaders.Add("Cookie", cookie);
        return client;
    }

    private static async Task<string?> CreateAsync(
        HttpClient client, string collection, JsonObject body, List<string> failures, FieldInterface face)
    {
        var response = await client.PostAsync(
            $"/api/items/{collection}", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.Created)
        {
            failures.Add($"{face}: REST create answered {(int)response.StatusCode}: {text}");
            return null;
        }
        return JsonNode.Parse(text)!["data"]!["id"]!.GetValue<string>();
    }

    private static async Task<int> VerifyAsync(
        HttpClient client, string collection, string id, FieldInterface face, JsonObject body, List<string> failures)
    {
        var read = await client.GetAsync($"/api/items/{collection}/{id}");
        var readText = await read.Content.ReadAsStringAsync();
        if (read.StatusCode != HttpStatusCode.OK)
        {
            failures.Add($"{face}: REST read answered {(int)read.StatusCode}: {readText}");
            return 0;
        }

        var data = JsonNode.Parse(readText)!["data"]!.AsObject();
        var count = 0;
        if (face == FieldInterface.Repeater)
        {
            var sentRow = body["rows"]![0]!.AsObject();
            var gotRow = data["rows"]?[0]?.AsObject();
            foreach (var c in SubCells())
            {
                count++;
                CompareValue(failures, $"Repeater sub-field {c.Face}|{c.Id}", sentRow[Camel(c.Name)], gotRow?[Camel(c.Name)]);
            }
            return count;
        }

        foreach (var c in OwnCells(face))
        {
            count++;
            var key = Camel(c.Name);
            CompareValue(failures, $"{face}|{c.Id}", body[key], data[key]);
            if (Filterable.Contains(face) && IsScalar(c.Id))
                await FilterAsync(client, collection, id, face, c, key, failures);
        }
        return count;
    }

    // The nullable cells written as null read back as null, and the null filters tell the two rows apart.
    private static async Task VerifyNullsAsync(
        HttpClient client, string collection, string valueId, FieldInterface face, List<string> failures)
    {
        var nullable = (face == FieldInterface.Repeater ? SubCells() : OwnCells(face)).Where(c => c.Id.EndsWith('?')).ToList();
        if (nullable.Count == 0) return;

        var body = new JsonObject { ["anchor"] = "null" };
        if (face == FieldInterface.Repeater)
        {
            var row = new JsonObject();
            foreach (var c in SubCells()) row[Camel(c.Name)] = c.Id.EndsWith('?') ? null : JsonNode.Parse(SampleJson(c.Face, c.Id));
            body["rows"] = new JsonArray(row);
        }
        else
        {
            // A column that is not nullable has no null to write: the row carries a value for every other cell.
            foreach (var c in OwnCells(face))
                body[Camel(c.Name)] = c.Id.EndsWith('?') ? null : JsonNode.Parse(SampleJson(face, c.Id));
        }

        var nullId = await CreateAsync(client, collection, body, failures, face);
        if (nullId is null) return;

        var read = await client.GetAsync($"/api/items/{collection}/{nullId}");
        var data = JsonNode.Parse(await read.Content.ReadAsStringAsync())!["data"]!.AsObject();
        foreach (var c in nullable)
        {
            var got = face == FieldInterface.Repeater ? data["rows"]?[0]?[Camel(c.Name)] : data[Camel(c.Name)];
            if (got is not null) failures.Add($"{face}|{c.Id}: written as null, read back {got.ToJsonString()}");
            if (face == FieldInterface.Repeater || !Filterable.Contains(face)) continue;

            var key = Camel(c.Name);
            var nulls = await IdsAsync(client, collection, $"filter[{key}][_null]=true");
            var values = await IdsAsync(client, collection, $"filter[{key}][_nnull]=true");
            if (!nulls.Contains(nullId) || nulls.Contains(valueId))
                failures.Add($"{face}|{c.Id}: _null did not select exactly the null row");
            if (values.Contains(nullId) || !values.Contains(valueId))
                failures.Add($"{face}|{c.Id}: _nnull did not select exactly the row with a value");
        }
    }

    private static async Task<HashSet<string>> IdsAsync(HttpClient client, string collection, string query)
    {
        var response = await client.GetAsync($"/api/items/{collection}?{query}&limit=100");
        var text = await response.Content.ReadAsStringAsync();
        return response.StatusCode == HttpStatusCode.OK
            ? JsonNode.Parse(text)!["data"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).ToHashSet()
            : [];
    }

    private static void CompareValue(List<string> failures, string cell, JsonNode? sent, JsonNode? got)
    {
        if (!JsonNode.DeepEquals(sent, got))
            failures.Add($"{cell}: sent {sent?.ToJsonString()}, read back {got?.ToJsonString() ?? "(missing)"}");
    }

    private static async Task FilterAsync(
        HttpClient client, string collection, string id, FieldInterface face, Cell cell, string key, List<string> failures)
    {
        var value = Uri.EscapeDataString(FilterText(face, cell.Id));
        var response = await client.GetAsync($"/api/items/{collection}?filter[{key}][_eq]={value}");
        var text = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != HttpStatusCode.OK)
        {
            failures.Add($"{face}|{cell.Id}: _eq filter answered {(int)response.StatusCode}: {text}");
            return;
        }

        var found = JsonNode.Parse(text)!["data"]!.AsArray().Any(n => n!["id"]!.GetValue<string>() == id);
        if (!found) failures.Add($"{face}|{cell.Id}: _eq filter with '{value}' did not find the record");
    }

    // ---- GraphQL: only the interfaces the schema exposes ----

    private static async Task<int> GraphQlAsync(
        HttpClient client, FieldInterface face, string collection, JsonObject body, List<string> failures)
    {
        var exposed = SchemaExposed(face);
        if (exposed.Count == 0) return 0;

        var selection = "id anchor " + string.Join(" ", exposed.Select(n => Selection(face, n)));
        var pascal = char.ToUpperInvariant(collection[0]) + collection[1..];
        var query = $"{{ {collection}s {{ items {{ {selection} }} }} }}";
        var queried = await PostGraphQlAsync(client, query, null);
        AddGraphQlErrors(failures, $"{face}: GraphQL query", queried);

        var input = new JsonObject { ["anchor"] = "gql" };
        foreach (var name in exposed) input[name] = GraphQlValue(face, body[name]);
        var mutation = $"mutation($input: {pascal}CreateInput!) {{ create{pascal}(input: $input) {{ id }} }}";
        var created = await PostGraphQlAsync(client, mutation, new JsonObject { ["input"] = input });
        AddGraphQlErrors(failures, $"{face}: GraphQL create", created);
        if (created["data"]?[$"create{pascal}"]?["id"]?.GetValue<string>() is not { } id)
        {
            failures.Add($"{face}: GraphQL create returned no id: {created.ToJsonString()}");
            return 1;
        }

        var single = await PostGraphQlAsync(client, $"{{ {collection}(id: \"{id}\") {{ {selection} }} }}", null);
        AddGraphQlErrors(failures, $"{face}: GraphQL read of the created row", single);
        if (single["data"]?[collection] is not JsonObject row)
        {
            failures.Add($"{face}: GraphQL read of the created row returned no row: {single.ToJsonString()}");
            return 1;
        }

        foreach (var name in exposed)
            CompareQueried(failures, face, name, input[name], row[name]);
        return 1;
    }

    // A Repeater compares row by row and sub-field by sub-field; every other field as one value.
    private static void CompareQueried(List<string> failures, FieldInterface face, string name, JsonNode? written, JsonNode? queried)
    {
        if (face != FieldInterface.Repeater)
        {
            if (!SameValue(written, queried))
                failures.Add($"{face}.{name}: GraphQL wrote {written?.ToJsonString()}, read {queried?.ToJsonString() ?? "(missing)"}");
            return;
        }

        foreach (var c in SubCells())
        {
            var key = Camel(c.Name);
            if (!SameValue(written?[0]?[key], queried?[0]?[key]))
                failures.Add($"Repeater sub-field {c.Face}|{c.Id}: GraphQL wrote {written?[0]?[key]?.ToJsonString()}, read {queried?[0]?[key]?.ToJsonString() ?? "(missing)"}");
        }
    }

    // Equal as JSON, with numbers compared by value and date-times by instant: GraphQL prints a DateTime
    // with milliseconds, REST without.
    private static bool SameValue(JsonNode? a, JsonNode? b)
    {
        if (JsonNode.DeepEquals(a, b)) return true;
        if (a is not JsonValue || b is not JsonValue) return false;
        if (a.GetValueKind() == JsonValueKind.Number && b.GetValueKind() == JsonValueKind.Number)
            return a.GetValue<decimal>() == b.GetValue<decimal>();
        return a.GetValueKind() == JsonValueKind.String && b.GetValueKind() == JsonValueKind.String
            && DateTimeOffset.TryParse(a.GetValue<string>(), out var left)
            && DateTimeOffset.TryParse(b.GetValue<string>(), out var right) && left.UtcDateTime == right.UtcDateTime;
    }

    private static List<string> SchemaExposed(FieldInterface face) =>
        face is FieldInterface.Hidden or FieldInterface.Divider or FieldInterface.Password
            ? []
            : face == FieldInterface.Repeater
                ? ["rows"]
                : OwnCells(face).Select(c => Camel(c.Name)).ToList();

    // Tags and the Repeater select their members; every other exposed field is a leaf.
    private static string Selection(FieldInterface face, string name) => face switch
    {
        FieldInterface.Repeater => "rows { " + string.Join(" ", SubCells().Select(c => Camel(c.Name))) + " }",
        FieldInterface.Tags => name + " { value label }",
        _ => name,
    };

    // The Date scalar takes the date part only.
    private static JsonNode? GraphQlValue(FieldInterface face, JsonNode? value)
    {
        var copy = value?.DeepClone();
        if (face == FieldInterface.Date && copy is JsonValue text && text.TryGetValue<string>(out var s))
            return JsonValue.Create(s[..10]);
        if (face != FieldInterface.Repeater || copy is not JsonArray rows) return copy;

        foreach (var c in SubCells().Where(c => c.Face == FieldInterface.Date))
            rows[0]![Camel(c.Name)] = JsonValue.Create(rows[0]![Camel(c.Name)]!.GetValue<string>()[..10]);
        return rows;
    }

    private static async Task<JsonNode> PostGraphQlAsync(HttpClient client, string query, JsonObject? variables)
    {
        var request = new JsonObject { ["query"] = query };
        if (variables is not null) request["variables"] = variables;
        var response = await client.PostAsync(
            "/graphql", new StringContent(request.ToJsonString(), new MediaTypeHeaderValue("application/json")));
        return JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
    }

    private static void AddGraphQlErrors(List<string> failures, string step, JsonNode response)
    {
        if (response["errors"] is JsonArray { Count: > 0 } errors)
            failures.Add($"{step}: {errors.ToJsonString()}");
    }
}
