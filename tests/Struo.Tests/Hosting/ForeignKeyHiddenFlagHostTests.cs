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
/// A ManyToOne foreign key whose field sets <c>Hidden = true</c> is not offered to a reader by name: REST and
/// GraphQL refuse it exactly as they refuse a hidden field that is not a key. The relation object next to it and
/// a write that names the key keep working, and a key whose interface is <c>Hidden</c> (the flag unset) stays
/// nameable. Stored values are read through a row type that maps the table directly.
/// </summary>
[Collection("ApiIntegration")]
public sealed class ForeignKeyHiddenFlagHostTests(ForeignKeyHiddenFlagHostTests.HostFixture fixture, ITestOutputHelper output)
    : IClassFixture<ForeignKeyHiddenFlagHostTests.HostFixture>
{
    private const string Flagged = "fkHiddenFlagOwner";
    private const string Interfaced = "fkHiddenFaceOwner";
    private const string KeyName = "ownerId";
    private const string MessageKey = "message";
    private const string ErrorsKey = "errors";
    private const string KeyProperty = "OwnerId";
    private const string SecretName = "secret";
    private const string TargetType = "FkHiddenTarget";

    [SugarTable("fkHiddenFlagOwner")]
    public sealed class FlaggedRow { [SugarColumn(IsPrimaryKey = true)] public Guid Id { get; set; } public Guid? OwnerId { get; set; } }

    private static readonly EmittedProperty Name = new("Name", typeof(string), FieldInterface.Text);

    public sealed class HostFixture : IAsyncLifetime
    {
        public ForkHost Host { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            (Host, Client) = await EmittedGraphQlHost.StartAsync(
                [
                    new EmittedEntity(TargetType, [Name]),
                    new EmittedEntity("FkHiddenFlagOwner",
                        [
                            Name,
                            new EmittedProperty("Secret", typeof(string), FieldInterface.Text, Hidden: true, Nullable: true),
                            new EmittedProperty(KeyProperty, typeof(Guid?), FieldInterface.Uuid, Hidden: true, Nullable: true),
                        ],
                        Relations: [new EmittedRelation("Owner", TargetType, KeyProperty)]),
                    new EmittedEntity("FkHiddenFaceOwner",
                        [Name, new EmittedProperty(KeyProperty, typeof(Guid?), FieldInterface.Hidden, Nullable: true)],
                        Relations: [new EmittedRelation("Owner", TargetType, KeyProperty)]),
                ],
                "fkhiddenflag");
        }

        public async Task DisposeAsync() => await Host.DisposeAsync();
    }

    private HttpClient Client => fixture.Client;

    private static string Tag() => "t" + Guid.NewGuid().ToString("N");

    private async Task<(HttpStatusCode Status, string Message)> GetAsync(string url)
    {
        var response = await Client.GetAsync(url);
        var text = await response.Content.ReadAsStringAsync();
        output.WriteLine($"GET {url} -> {(int)response.StatusCode}: {text}");
        return (response.StatusCode, JsonNode.Parse(text)?["error"]?[MessageKey]?.GetValue<string>() ?? "");
    }

    private async Task<JsonNode> GetOkAsync(string url)
    {
        var response = await Client.GetAsync(url);
        var text = await response.Content.ReadAsStringAsync();
        output.WriteLine($"GET {url} -> {(int)response.StatusCode}: {text}");
        response.StatusCode.Should().Be(HttpStatusCode.OK, text);
        return JsonNode.Parse(text)!;
    }

    private async Task<JsonNode> GraphQlRawAsync(string query)
    {
        var body = new JsonObject { ["query"] = query }.ToJsonString();
        var response = await Client.PostAsync("/graphql", new StringContent(body, Encoding.UTF8, "application/json"));
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        output.WriteLine(json.ToJsonString());
        return json;
    }

    private async Task<(string Target, string Owner, string Tag)> SeedAsync(string collection)
    {
        var tag = Tag();
        var target = await EmittedGraphQlHost.CreateAsync(Client, "fkHiddenTarget", new JsonObject { ["name"] = "target-" + tag });
        var owner = await EmittedGraphQlHost.CreateAsync(Client, collection, new JsonObject { ["name"] = tag, [KeyName] = target });
        return (target, owner, tag);
    }

    private async Task<Guid?> StoredKeyAsync(string id)
    {
        using var scope = fixture.Host.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        var guid = Guid.Parse(id);
        return (await db.Queryable<FlaggedRow>().Where(r => r.Id == guid).FirstAsync()).OwnerId;
    }

    public static TheoryData<string> KeyQueries() => new()
    {
        "filter[{0}][_eq]=00000000-0000-0000-0000-000000000001",
        "sort={0}",
        "fields=name,{0}",
        "facets={0}",
        "aggregate[count]={0}",
    };

    [Theory]
    [MemberData(nameof(KeyQueries))]
    public async Task REST_refuses_the_hidden_key_by_name_as_it_refuses_a_hidden_field_that_is_not_a_key(string template)
    {
        var reference = await GetAsync($"/api/items/{Flagged}?" + string.Format(template, SecretName));
        var key = await GetAsync($"/api/items/{Flagged}?" + string.Format(template, KeyName));

        reference.Status.Should().Be(HttpStatusCode.BadRequest);
        key.Status.Should().Be(HttpStatusCode.BadRequest);
        key.Message.Should().Be($"Unknown field 'ownerId' on collection '{Flagged}'.");
        key.Message.Should().Be(reference.Message.Replace(SecretName, KeyName));
    }

    [Fact]
    public async Task A_key_whose_interface_is_Hidden_with_the_flag_unset_is_still_named_by_a_reader()
    {
        var (target, _, tag) = await SeedAsync(Interfaced);

        var json = await GetOkAsync($"/api/items/{Interfaced}?filter[name][_eq]={tag}&filter[ownerId][_eq]={target}&sort=ownerId");

        json["meta"]!["total"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task The_graphql_inputs_and_filter_do_not_offer_the_hidden_key_and_the_relation_filter_works()
    {
        var (_, ownerId, tag) = await SeedAsync(Flagged);

        var named = await GraphQlRawAsync($$"""{ fkHiddenFlagOwners(filter: { ownerId: { eq: "{{Guid.NewGuid()}}" } }) { items { id } } }""");
        var create = await GraphQlRawAsync($$"""mutation { createFkHiddenFlagOwner(input: { name: "{{Tag()}}", ownerId: "{{Guid.NewGuid()}}" }) { id } }""");
        var update = await GraphQlRawAsync($$"""mutation { updateFkHiddenFlagOwner(id: "{{ownerId}}", input: { ownerId: "{{Guid.NewGuid()}}" }) { id } }""");
        var related = await GraphQlRawAsync($$"""{ fkHiddenFlagOwners(filter: { name: { eq: "{{tag}}" }, owner: { name: { eq: "target-{{tag}}" } } }) { items { id owner { name } } } }""");

        named[ErrorsKey]![0]![MessageKey]!.GetValue<string>().Should().Contain(KeyName);
        create[ErrorsKey]![0]![MessageKey]!.GetValue<string>().Should().Contain(KeyName);
        update[ErrorsKey]![0]![MessageKey]!.GetValue<string>().Should().Contain(KeyName);
        related[ErrorsKey].Should().BeNull();
        related["data"]!["fkHiddenFlagOwners"]!["items"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).Should().Equal(ownerId);
    }

    [Fact]
    public async Task A_REST_body_that_names_the_hidden_key_writes_it_and_the_item_read_does_not_return_it()
    {
        var (target, id, _) = await SeedAsync(Flagged);
        var other = await EmittedGraphQlHost.CreateAsync(Client, "fkHiddenTarget", new JsonObject { ["name"] = Tag() });
        (await StoredKeyAsync(id)).Should().Be(Guid.Parse(target));

        var put = new StringContent(new JsonObject { [KeyName] = other }.ToJsonString(), Encoding.UTF8, "application/json");
        (await Client.PutAsync($"/api/items/{Flagged}/{id}", put)).StatusCode.Should().Be(HttpStatusCode.OK);

        (await StoredKeyAsync(id)).Should().Be(Guid.Parse(other));
        var item = (await GetOkAsync($"/api/items/{Flagged}/{id}"))["data"]!.AsObject();
        item.ContainsKey(KeyName).Should().BeFalse();
    }

    [Fact]
    public async Task The_relation_object_stays_reachable_through_expand_filter_sort_and_facets()
    {
        var (target, id, tag) = await SeedAsync(Flagged);

        var deep = await GetOkAsync($"/api/items/{Flagged}/{id}?deep=owner");
        var filtered = await GetOkAsync($"/api/items/{Flagged}?filter[name][_eq]={tag}&filter[owner.name][_eq]=target-{tag}&sort=owner.name&facets=owner");

        deep["data"]!["owner"]!["id"]!.GetValue<string>().Should().Be(target);
        filtered["meta"]!["total"]!.GetValue<int>().Should().Be(1);
        filtered["meta"]!["facets"]!["owner"]![0]!["value"]!.GetValue<string>().Should().Be(target);
    }

    [Fact]
    public async Task The_schema_endpoint_lists_no_field_for_the_hidden_key()
    {
        var schema = (await GetOkAsync($"/api/schema/{Flagged}"))["data"]!;

        schema["fields"]!.AsArray().Select(f => f!["name"]!.GetValue<string>()).Should().NotContain([KeyName, SecretName]);
    }
}
