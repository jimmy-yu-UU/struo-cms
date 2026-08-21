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

    // MaxLength is deliberately never published (see PasswordPolicyOptions' remarks: it is a sanity
    // bound, not a security control, and publishing it would give nothing but a future author's
    // temptation to claim otherwise). Nothing else pins that asymmetry down, so a future author adding
    // passwordMaxLength here would break nothing today.
    [Fact]
    public async Task Config_does_not_publish_a_password_maximum()
    {
        var body = await factory.CreateClient().GetStringAsync("/api/config");

        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("data").TryGetProperty("passwordMaxLength", out _).Should().BeFalse();
    }
}
