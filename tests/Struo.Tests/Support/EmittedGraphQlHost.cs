using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using SqlSugar;
using Struo.Application.Metadata;
using Xunit.Abstractions;

namespace Struo.Tests.Support;

/// <summary>
/// A host with one emitted collection on the backend <c>STRUO_TEST_BACKEND</c> selects, its tables created and
/// an admin signed in, for tests that read and write the collection through REST and GraphQL.
/// </summary>
internal static class EmittedGraphQlHost
{
    public static async Task<(ForkHost Host, HttpClient Client)> StartAsync(EmittedEntity entity, string assemblyPrefix)
    {
        var collection = char.ToLowerInvariant(entity.Name[0]) + entity.Name[1..];
        var assembly = EmittedEntities.Build(EmittedEntities.UniqueName(assemblyPrefix), [entity]);
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
            CreateTables(host, collection);
            return (host, await LoginAsync(host));
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private static void CreateTables(ForkHost host, string collection)
    {
        var registry = host.App.Services.GetRequiredService<IEntityRegistry>();
        var sidecar = host.App.Services.GetRequiredService<IMetadataProvider>().GetCollection(collection)!.Translation?.TranslationEntityType;
        using var scope = host.App.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ISqlSugarClient>();
        db.CodeFirst.InitTables(registry.Get(collection)!.EntityType);
        if (sidecar is not null) db.CodeFirst.InitTables(sidecar);
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

    /// <summary>Creates a record through REST, which stores the value as sent, and returns its id.</summary>
    public static async Task<string> CreateAsync(HttpClient client, string collection, JsonObject body)
    {
        var response = await client.PostAsync($"/api/items/{collection}", new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"));
        var text = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, text);
        return JsonNode.Parse(text)!["data"]!["id"]!.GetValue<string>();
    }

    /// <summary>Runs a GraphQL query, writes the response to the test output and fails on an error.</summary>
    public static async Task<JsonNode> QueryAsync(HttpClient client, ITestOutputHelper output, string query)
    {
        var request = new JsonObject { ["query"] = query };
        var response = await client.PostAsync("/graphql", new StringContent(request.ToJsonString(), Encoding.UTF8, "application/json"));
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync())!;
        output.WriteLine(json.ToJsonString());
        json["errors"].Should().BeNull(json.ToJsonString());
        return json;
    }
}
