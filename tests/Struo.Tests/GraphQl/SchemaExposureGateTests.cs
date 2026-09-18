// tests/Struo.Tests/GraphQl/SchemaExposureGateTests.cs
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

/// <summary>
/// Schema DISCLOSURE is gated consistently across its routes, and gating it does not touch query
/// EXECUTION.
/// <para>
/// There are three ways to read the schema. Two were gated outside Development — introspection queries
/// (<c>DisableIntrospection</c>) and the Nitro browser IDE (<c>Tool.Enable</c>) — while the third,
/// HotChocolate's built-in <c>GET /graphql?sdl</c>, was not gated at all and served the complete SDL to
/// an anonymous caller in Production. A reader who stopped at "introspection is refused outside
/// Development" would wrongly conclude the schema was unreachable there.
/// </para>
/// <para>
/// GraphQL is a first-class consumption path for forks of this template, not dead weight, so what makes
/// closing this safe is the distinction these tests pin: <c>?sdl</c> and introspection are schema
/// DISCOVERY, whereas a client executes through <c>POST /graphql</c> with a query document. Closing
/// discovery leaves execution untouched. Discovery stays open in Development, which is where
/// schema-dependent tooling (codegen, Postman import, Apollo Sandbox) belongs, and
/// <c>GraphQl:ExposeSchema</c> lets a fork that deliberately publishes a public GraphQL API turn it
/// back on in Production without a rebuild.
/// </para>
/// <para>
/// NOTE on structure: the default-Production assertions are deliberately ONE test sharing ONE derived
/// host. Standing up several <c>WebApplicationFactory</c> hosts against this collection's single shared
/// SQLite database makes some of them fail requests with a 500, which is a test-harness artifact
/// rather than product behavior — asserting only "not 200" would let that 500 pass silently. Every
/// status below is therefore asserted exactly.
/// </para>
/// </summary>
[Collection("ApiIntegration")]
public class SchemaExposureGateTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // language is in ApiFactory's Rbac:PublicReadCollections, so this resolves for an anonymous caller
    // through the RBAC public floor — no cookie needed, which matters because a Production-environment
    // host forces CookieSecurePolicy.Always while the test host speaks HTTP.
    private const string PublicQuery = "{ languages { items { id } } }";
    private const string IntrospectionQuery = "{ __schema { queryType { name } } }";

    private HttpClient CreateProductionClient(bool? exposeSchema = null) =>
        _factory
            .WithWebHostBuilder(b =>
            {
                b.UseEnvironment("Production");
                if (exposeSchema is not null)
                    b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(
                        new Dictionary<string, string?>
                        {
                            ["GraphQl:ExposeSchema"] = exposeSchema.Value ? "true" : "false",
                        }));
            })
            .CreateClient();

    [Fact]
    public async Task Production_closes_schema_disclosure_but_keeps_executing_queries()
    {
        var client = CreateProductionClient();

        // 1. The ?sdl route is gone. Measured status is 404 with an empty body; asserted exactly so a
        //    broken host (500) can never be mistaken for a blocked route.
        var sdl = await client.GetAsync("/graphql?sdl");
        var sdlBody = await sdl.Content.ReadAsStringAsync();
        sdl.StatusCode.Should().Be(HttpStatusCode.NotFound, sdlBody);
        sdlBody.Should().NotContain("type Query", "the full SDL must not reach an anonymous caller");

        // 2. Introspection stays refused (pre-existing gate, pinned here so the two disclosure routes
        //    cannot drift apart silently — that drift is what produced this finding).
        var introspection = await client.PostAsJsonAsync("/graphql", new { query = IntrospectionQuery });
        var introspectionBody = await introspection.Content.ReadAsStringAsync();
        introspection.StatusCode.Should().Be(HttpStatusCode.BadRequest, introspectionBody);
        introspectionBody.Should().Contain("Introspection is not allowed");

        // 3. THE LOAD-BEARING ASSERTION for forks consuming via GraphQL: execution is untouched. If
        //    this fails, the gate went too far and broke the consumption path it was meant to preserve.
        var executed = await client.PostAsJsonAsync("/graphql", new { query = PublicQuery });
        var executedBody = await executed.Content.ReadAsStringAsync();
        executed.StatusCode.Should().Be(HttpStatusCode.OK, executedBody);
        using var doc = JsonDocument.Parse(executedBody);
        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse(executedBody);
        doc.RootElement.GetProperty("data").GetProperty("languages").GetProperty("items")
            .ValueKind.Should().Be(JsonValueKind.Array);
    }

    [Fact]
    public async Task Production_with_ExposeSchema_true_serves_the_sdl_route_again()
    {
        // A fork publishing a public GraphQL API opts back in by configuration alone
        // (GraphQl__ExposeSchema=true), with no rebuild.
        var client = CreateProductionClient(exposeSchema: true);

        var response = await client.GetAsync("/graphql?sdl");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().Contain("type Query");
    }

    [Fact]
    public async Task Development_serves_the_sdl_route_so_codegen_tooling_still_works()
    {
        // Unset config keeps the default environment-driven behavior, so the schema-dependent workflow
        // a fork uses to BUILD a GraphQL frontend is unaffected. ApiFactory's base host is Development.
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/graphql?sdl");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, body);
        body.Should().Contain("type Query");
    }
}
