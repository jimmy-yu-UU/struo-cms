// tests/Struo.Tests/Api/EndpointSmokeTests.cs
using System.Net;
using FluentAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

public class EndpointSmokeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private readonly ApiFactory _factory = factory;

    [Fact]
    public async Task Liveness_returns_200()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Readiness_returns_200_when_db_reachable()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Ping_returns_ok_payload_in_camelCase()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/api/ping");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":\"ok\"");
        body.Should().Contain("\"service\":\"StruoCMS\"");
    }
}
