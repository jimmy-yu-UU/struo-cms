// tests/Struo.Tests/GraphQl/GraphQlEndpointSmokeTests.cs
using System.Net;
using System.Net.Http.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.GraphQl;

[Collection("ApiIntegration")]
public class GraphQlEndpointSmokeTests(ApiFactory factory)
{
    private readonly ApiFactory _factory = factory;

    // Regression guard: the request executor (schema build) failed at host-startup time when the
    // error filter was registered via IRequestExecutorBuilder.AddErrorFilter<T>(), because the
    // schema-services container HotChocolate builds for the pipeline excludes ILogger<T> — see
    // GraphQlServiceCollectionExtensions. This proves /graphql is actually reachable end-to-end,
    // not just that DI registration compiles.
    [Fact]
    public async Task Service_query_returns_ok_and_marker_string()
    {
        var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/graphql", new { query = "{ _service }" });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("StruoCMS GraphQL");
        body.Should().NotContain("\"errors\"");
    }
}
