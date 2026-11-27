using System.Net;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Struo.Domain.Metadata;
using Struo.Domain.Metadata.Enums;
using Struo.Tests.Support;
using Xunit;
using Xunit.Abstractions;

namespace Struo.Tests.Hosting;

/// <summary>
/// The rows of a one-to-many relation whose child key sets <c>Hidden = true</c> are read through the child's many-to-one
/// relation back to the parent: the filter on the related row's id lists them, while the key itself is refused by name.
/// A host whose child has no relation back does not start.
/// </summary>
[Collection("ApiIntegration")]
public sealed class HiddenChildKeyHostTests(HiddenChildKeyHostTests.HostFixture fixture, ITestOutputHelper output)
    : IClassFixture<HiddenChildKeyHostTests.HostFixture>
{
    private const string Parent = "hckHostParent";
    private const string Child = "hckHostChild";
    private const string KeyName = "parentId";
    private const string DataKey = "data";

    private static readonly EmittedProperty Name = new("Name", typeof(string), FieldInterface.Text);

    public sealed class HostFixture : IAsyncLifetime
    {
        public ForkHost Host { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;

        public async Task InitializeAsync()
        {
            (Host, Client) = await EmittedGraphQlHost.StartAsync(
                [
                    new EmittedEntity("HckHostParent", [Name],
                        Relations: [new EmittedRelation("Kids", "HckHostChild", "ParentId", ToMany: true)]),
                    new EmittedEntity("HckHostChild",
                        [Name, new EmittedProperty("ParentId", typeof(Guid?), FieldInterface.Uuid, Hidden: true, Nullable: true)],
                        Relations: [new EmittedRelation("Parent", "HckHostParent", "ParentId")]),
                ],
                "hckhost");
        }

        public async Task DisposeAsync() => await Host.DisposeAsync();
    }

    private HttpClient Client => fixture.Client;

    private async Task<(HttpStatusCode Status, JsonNode Body)> GetAsync(string url)
    {
        var response = await Client.GetAsync(url);
        var text = await response.Content.ReadAsStringAsync();
        output.WriteLine($"GET {url} -> {(int)response.StatusCode}: {text}");
        return (response.StatusCode, JsonNode.Parse(text)!);
    }

    private static string Tag() => "t" + Guid.NewGuid().ToString("N");

    private async Task<(string Parent, string Child, string Tag)> SeedAsync()
    {
        var tag = Tag();
        var parent = await EmittedGraphQlHost.CreateAsync(Client, Parent, new JsonObject { ["name"] = "p-" + tag });
        var child = await EmittedGraphQlHost.CreateAsync(Client, Child, new JsonObject { ["name"] = tag, [KeyName] = parent });
        await EmittedGraphQlHost.CreateAsync(Client, Child, new JsonObject { ["name"] = "other-" + tag });
        return (parent, child, tag);
    }

    [Fact]
    public async Task The_children_are_listed_by_the_id_of_the_related_parent()
    {
        var (parent, child, _) = await SeedAsync();

        var (status, body) = await GetAsync($"/api/items/{Child}?filter[parent.id][_eq]={parent}");

        status.Should().Be(HttpStatusCode.OK);
        body[DataKey]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).Should().Equal(child);
        body["meta"]!["total"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public async Task The_hidden_key_itself_is_refused_by_name()
    {
        var (parent, _, _) = await SeedAsync();

        var (status, body) = await GetAsync($"/api/items/{Child}?filter[{KeyName}][_eq]={parent}");

        status.Should().Be(HttpStatusCode.BadRequest);
        body["error"]!["message"]!.GetValue<string>().Should().Be($"Unknown field '{KeyName}' on collection '{Child}'.");
    }

    [Fact]
    public async Task Deep_expansion_of_the_relation_lists_the_children_without_naming_the_key()
    {
        var (parent, child, _) = await SeedAsync();

        var (status, body) = await GetAsync($"/api/items/{Parent}/{parent}?deep=kids");

        status.Should().Be(HttpStatusCode.OK);
        body[DataKey]!["kids"]!.AsArray().Select(n => n!["id"]!.GetValue<string>()).Should().Equal(child);
    }

    [Fact]
    public async Task A_child_with_a_hidden_key_and_no_relation_back_does_not_start()
    {
        var content = EmittedEntities.Build(
            EmittedEntities.UniqueName("hckbare"),
            [
                new EmittedEntity("HckBareParent", [Name],
                    Relations: [new EmittedRelation("Kids", "HckBareChild", "ParentId", ToMany: true)]),
                new EmittedEntity("HckBareChild",
                    [Name, new EmittedProperty("ParentId", typeof(Guid?), FieldInterface.Uuid, Hidden: true, Nullable: true)]),
            ]).GetName().Name!;

        var error = await ForkHost.StartupErrorAsync(
            mutate: s => s["Struo:ContentAssemblies:1"] = content, environment: "Production");

        output.WriteLine(error?.ToString());
        error.Should().BeOfType<MetadataException>().Which.Message
            .Should().Contain("Relation 'HckBareParent.Kids'").And.Contain("'HckBareChild.ParentId'")
            .And.Contain("Hidden = true");
    }
}
