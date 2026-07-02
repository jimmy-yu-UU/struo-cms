// tests/Struo.Tests/Api/LanguagesEndpointTests.cs
using System.Net;
using System.Text.Json;
using AwesomeAssertions;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

[Collection("ApiIntegration")]
public sealed class LanguagesEndpointTests
{
    [Fact]
    public async Task Languages_ReturnsEnabledLocales_WithExactlyOneDefault()
    {
        using var factory = new ApiFactory();
        var client = await factory.CreateAuthenticatedClientAsync();

        var res = await client.GetAsync("/api/languages");
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var arr = doc.RootElement.GetProperty("data");
        arr.GetArrayLength().Should().BeGreaterThan(0);
        arr.EnumerateArray().Count(e => e.GetProperty("isDefault").GetBoolean()).Should().Be(1);
        arr.EnumerateArray().Select(e => e.GetProperty("code").GetString())
           .Should().Contain("en");
    }

    [Fact]
    public async Task Languages_Anonymous_Returns401()
    {
        using var factory = new ApiFactory();
        var client = factory.CreateClient();
        var res = await client.GetAsync("/api/languages");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
