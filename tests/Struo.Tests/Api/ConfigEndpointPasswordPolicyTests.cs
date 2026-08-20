using System.Collections.Generic;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Api;

// The SPA validates the new password inline and sizes its generator from this value, so it must
// follow Auth:Password:MinLength rather than being a second hardcoded 8 on the client.
[Collection("ApiIntegration")]
public class ConfigEndpointPasswordPolicyTests(ApiFactory factory)
{
    [Fact]
    public async Task Config_publishes_the_configured_password_minimum()
    {
        var host = factory.WithWebHostBuilder(b =>
            b.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Auth:Password:MinLength"] = "14",
            })));

        var body = await host.CreateClient().GetStringAsync("/api/config");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("data").GetProperty("passwordMinLength").GetInt32().Should().Be(14);
    }
}
