using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Domain.Metadata.Enums;
using Struo.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Struo.Tests.Hosting;

/// <summary>
/// A ManyToOne foreign key that is also a writable <c>[CmsField]</c> is written, filtered and sorted through
/// REST and GraphQL in the form the field declares: its scalar in the create and update inputs, and its own
/// operators in the filter input (<c>gt</c> on a number key, <c>contains</c> on a text key). Stored values are
/// read through row types that map the tables directly. Runs on the backend <c>STRUO_TEST_BACKEND</c> selects.
/// </summary>
[Collection("ApiIntegration")]
public sealed class ForeignKeyOwnFormHostTests(ForeignKeyOwnFormHostTests.HostFixture fixture, ITestOutputHelper output)
    : IClassFixture<ForeignKeyOwnFormHostTests.HostFixture>
{
    private const string Uuid = "Uuid";
    private const string Int = "Int";
    private const string Long = "Long";
    private const string Text = "Text";

    private static readonly string[] Kinds = [Uuid, Int, Long, Text];

    // The key values of a case in ascending order, a filter only the own form of the key has, and the indexes
    // of the values that filter keeps.
    private sealed record KeyCase(
        string Collection, JsonNode[] Keys, string GraphQlFilter, string RestFilter, int[] FilterHits);

    private static readonly Dictionary<string, KeyCase> Cases = new()
    {
        [Uuid] = new("fkUuidOwner",
            [Id(1), Id(2), Id(3)], $$"""{ in: [{{Id(1).ToJsonString()}}, {{Id(3).ToJsonString()}}] }""",
            $"[_in]={Id(1).GetValue<string>()},{Id(3).GetValue<string>()}", [0, 2]),
        [Int] = new("fkIntOwner", [10, 20, 30], "{ gt: 15 }", "[_gt]=15", [1, 2]),
        [Long] = new("fkLongOwner", [10L, 20L, 5_000_000_000L], "{ gt: 15 }", "[_gt]=15", [1, 2]),
        [Text] = new("fkTextOwner", ["alpha-1", "alpha-3", "beta-2"], """{ contains: "alpha" }""", "[_contains]=alpha", [0, 1]),
    };

    public static TheoryData<string> AllKinds() => new(Kinds);

    private static JsonValue Id(int n) => JsonValue.Create($"00000000-0000-0000-0000-00000000000{n}");

    [SugarTable("fkUuidOwner")]
    public sealed class UuidRow { [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; } public Guid? OwnerId { get; set; } }

    [SugarTable("fkIntOwner")]
    public sealed class IntRow { [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; } public int? OwnerId { get; set; } }

    [SugarTable("fkLongOwner")]
    public sealed class LongRow { [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; } public long? OwnerId { get; set; } }

    [SugarTable("fkTextOwner")]
    public sealed class TextRow { [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; } public string? OwnerId { get; set; } }

    private static readonly EmittedProperty Name = new("Name", typeof(string), FieldInterface.Text);

    private static EmittedEntity Target(string name, Type? idType) => new(name, [Name], IdType: idType);

    private static EmittedEntity Owner(string name, string target, Type keyType, FieldInterface face) => new(
        name, [Name, new EmittedProperty("OwnerId", keyType, face, Nullable: true)],
        Relations: [new EmittedRelation("Owner", target, "OwnerId")]);

    public sealed class HostFixture : IAsyncLifetime
    {
        public ForkHost Host { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            (Host, Client) = await EmittedGraphQlHost.StartAsync(
                [
                    Target("FkUuidTarget", null), Target("FkIntTarget", typeof(int)),
                    Target("FkLongTarget", typeof(long)), Target("FkTextTarget", typeof(string)),
                    Owner("FkUuidOwner", "FkUuidTarget", typeof(Guid?), FieldInterface.Uuid),
                    Owner("FkIntOwner", "FkIntTarget", typeof(int?), FieldInterface.Number),
                    Owner("FkLongOwner", "FkLongTarget", typeof(long?), FieldInterface.Number),
                    Owner("FkTextOwner", "FkTextTarget", typeof(string), FieldInterface.Text),
                ],
                "fkownform");
        }

        public async Task DisposeAsync() => await Host.DisposeAsync();
    }

    private HttpClient Client => fixture.Client;

    private static string Pascal(string collection) => char.ToUpperInvariant(collection[0]) + collection[1..];

    private async Task<object?> StoredAsync(string kind, string id)
    {
        using var scope = fixture.Host.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var guid = Guid.Parse(id);
        return kind switch
        {
            Uuid => (await db.Queryable<UuidRow>().Where(r => r.Id == guid).FirstAsync()).OwnerId,
            Int => (await db.Queryable<IntRow>().Where(r => r.Id == guid).FirstAsync()).OwnerId,
            Long => (await db.Queryable<LongRow>().Where(r => r.Id == guid).FirstAsync()).OwnerId,
            _ => (await db.Queryable<TextRow>().Where(r => r.Id == guid).FirstAsync()).OwnerId,
        };
    }

    private static object Expected(string kind, JsonNode key) => kind switch
    {
        Uuid => Guid.Parse(key.GetValue<string>()),
        Int => key.GetValue<int>(),
        Long => key.GetValue<long>(),
        _ => key.GetValue<string>(),
    };

    private static string Tag() => "t" + Guid.NewGuid().ToString("N");

    private async Task<string> RestCreateAsync(KeyCase c, string tag, JsonNode key) =>
        await EmittedGraphQlHost.CreateAsync(Client, c.Collection, new JsonObject { ["name"] = tag, ["ownerId"] = key.DeepClone() });

    // Creates the three rows through REST in the order 2, 0, 1; returns their ids by key index.
    private async Task<string[]> SeedAsync(KeyCase c, string tag)
    {
        var ids = new string[c.Keys.Length];
        foreach (var i in new[] { 2, 0, 1 }) ids[i] = await RestCreateAsync(c, tag, c.Keys[i]);
        return ids;
    }

    private async Task<JsonNode> GraphQlAsync(string query) => await EmittedGraphQlHost.QueryAsync(Client, output, query);

    private async Task<JsonNode> RestAsync(HttpMethod method, string url, JsonObject? body = null)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        var response = await Client.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        output.WriteLine($"{method} {url} -> {(int)response.StatusCode}: {text}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonNode.Parse(text)!;
    }

    private static List<string> Ids(JsonNode data) => data["data"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).ToList();

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task A_REST_create_stores_the_key(string kind)
    {
        var c = Cases[kind];

        var id = await RestCreateAsync(c, Tag(), c.Keys[1]);

        (await StoredAsync(kind, id)).Should().Be(Expected(kind, c.Keys[1]));
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task A_REST_update_stores_the_key(string kind)
    {
        var c = Cases[kind];
        var id = await RestCreateAsync(c, Tag(), c.Keys[0]);

        await RestAsync(HttpMethod.Put, $"/api/items/{c.Collection}/{id}", new JsonObject { ["ownerId"] = c.Keys[2].DeepClone() });

        (await StoredAsync(kind, id)).Should().Be(Expected(kind, c.Keys[2]));
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task A_REST_filter_with_an_operator_of_the_own_form_keeps_the_expected_rows(string kind)
    {
        var c = Cases[kind];
        var tag = Tag();
        var ids = await SeedAsync(c, tag);

        var found = Ids(await RestAsync(HttpMethod.Get, $"/api/items/{c.Collection}?filter[name][_eq]={tag}&filter[ownerId]{c.RestFilter}"));

        found.Should().BeEquivalentTo(c.FilterHits.Select(i => ids[i]));
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task A_REST_sort_by_the_key_orders_the_rows(string kind)
    {
        var c = Cases[kind];
        var tag = Tag();
        var ids = await SeedAsync(c, tag);

        var ascending = Ids(await RestAsync(HttpMethod.Get, $"/api/items/{c.Collection}?filter[name][_eq]={tag}&sort=ownerId"));
        var descending = Ids(await RestAsync(HttpMethod.Get, $"/api/items/{c.Collection}?filter[name][_eq]={tag}&sort=-ownerId"));

        ascending.Should().Equal(ids);
        descending.Should().Equal(ids.Reverse());
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task A_GraphQL_create_stores_the_key(string kind)
    {
        var c = Cases[kind];
        var pascal = Pascal(c.Collection);

        var json = await GraphQlAsync(
            $$"""mutation { create{{pascal}}(input: { name: "{{Tag()}}", ownerId: {{c.Keys[1].ToJsonString()}} }) { id ownerId } }""");

        var created = json["data"]![$"create{pascal}"]!;
        (await StoredAsync(kind, created["id"]!.GetValue<string>())).Should().Be(Expected(kind, c.Keys[1]));
        JsonNode.DeepEquals(created["ownerId"], c.Keys[1]).Should().BeTrue(created.ToJsonString());
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task A_GraphQL_update_stores_the_key(string kind)
    {
        var c = Cases[kind];
        var pascal = Pascal(c.Collection);
        var id = await RestCreateAsync(c, Tag(), c.Keys[0]);

        await GraphQlAsync(
            $$"""mutation { update{{pascal}}(id: "{{id}}", input: { ownerId: {{c.Keys[2].ToJsonString()}} }) { id } }""");

        (await StoredAsync(kind, id)).Should().Be(Expected(kind, c.Keys[2]));
    }

    [Theory]
    [MemberData(nameof(AllKinds))]
    public async Task A_GraphQL_filter_with_an_operator_of_the_own_form_keeps_the_expected_rows(string kind)
    {
        var c = Cases[kind];
        var tag = Tag();
        var ids = await SeedAsync(c, tag);

        var json = await GraphQlAsync(
            $$"""{ {{c.Collection}}s(filter: { name: { eq: "{{tag}}" }, ownerId: {{c.GraphQlFilter}} }) { items { id } } }""");

        var found = json["data"]![$"{c.Collection}s"]!["items"]!.AsArray().Select(n => n!["id"]!.GetValue<string>());
        found.Should().BeEquivalentTo(c.FilterHits.Select(i => ids[i]));
    }

    [Fact]
    public async Task A_relation_object_next_to_the_key_still_filters_through_its_own_name()
    {
        var c = Cases[Uuid];
        var tag = Tag();
        var hitName = "hit" + tag;
        var hitTarget = await EmittedGraphQlHost.CreateAsync(Client, "fkUuidTarget", new JsonObject { ["name"] = hitName });
        var missTarget = await EmittedGraphQlHost.CreateAsync(Client, "fkUuidTarget", new JsonObject { ["name"] = "miss" + tag });
        var hitOwner = await RestCreateAsync(c, tag, JsonValue.Create(hitTarget));
        await RestCreateAsync(c, tag, JsonValue.Create(missTarget));

        var json = await GraphQlAsync(
            $$"""{ {{c.Collection}}s(filter: { name: { eq: "{{tag}}" }, ownerId: { in: ["{{hitTarget}}", "{{missTarget}}"] }, owner: { name: { eq: "{{hitName}}" } } }) { items { id } } }""");

        var found = json["data"]![$"{c.Collection}s"]!["items"]!.AsArray().Select(n => n!["id"]!.GetValue<string>());
        found.Should().Equal(hitOwner);
    }
}
