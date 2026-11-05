using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Struo.Application.Security;
using Struo.Tests.Support;
using Xunit;

namespace Struo.Tests.Hosting;

/// <summary>A fork's <see cref="ISystemCapabilitySource"/> is validated at startup, not by the first request.</summary>
[Collection("ApiIntegration")]
public class CapabilitySourceStartupTests
{
    private sealed class Source(string code) : ISystemCapabilitySource
    {
        public IReadOnlyList<SystemCapability> Capabilities { get; } = [new(code, "k.label", "k.description")];
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task A_registered_source_adds_its_code_to_the_catalog()
    {
        await using var host = await ForkHost.StartAsync(
            afterAddStruoCms: s => s.AddSingleton<ISystemCapabilitySource>(new Source("reports.export")));

        host.App.Services.GetRequiredService<SystemCapabilityCatalog>().IsKnown("reports.export").Should().BeTrue();
    }

    [SqliteOnlyFact("starts a full host on a SQLite temp database")]
    public async Task A_source_repeating_a_core_code_fails_startup()
    {
        var error = await ForkHost.StartupErrorAsync(
            afterAddStruoCms: s => s.AddSingleton<ISystemCapabilitySource>(new Source("login.unlock")));

        error.Should().NotBeNull();
        error!.ToString().Should().Contain("login.unlock");
    }
}
