namespace Struo.Tests.Support;

/// <summary>
/// Pins the configuration a test host must see while services are registered. <c>AddStruoCms</c> reads
/// <c>Struo:ContentAssemblies</c>, Redis and the OIDC providers from <c>builder.Configuration</c> before
/// <c>builder.Build()</c>, which is earlier than <c>WebApplicationFactory</c> splices its
/// <c>ConfigureAppConfiguration</c> overrides in. Environment variables are folded into the builder
/// during <c>WebApplication.CreateBuilder</c>, so they are the only override that arrives in time.
/// </summary>
public static class TestHostEnvironment
{
    /// <summary>
    /// Sets the content assemblies every host in the process loads, and turns Redis and the OIDC
    /// providers off so a developer's gitignored Development settings cannot reach the suite. Call it
    /// from a <c>[ModuleInitializer]</c> in each test project.
    /// </summary>
    public static void Pin(params string[] contentAssemblies)
    {
        for (var i = 0; i < contentAssemblies.Length; i++)
            Environment.SetEnvironmentVariable($"Struo__ContentAssemblies__{i}", contentAssemblies[i]);

        // A single space is "not configured" to the Redis reader; setting a variable to the empty
        // string would delete it on Windows.
        Environment.SetEnvironmentVariable("Redis__ConnectionString", " ");
        Environment.SetEnvironmentVariable("Oidc__Providers__microsoft__Enabled", "false");
        Environment.SetEnvironmentVariable("Oidc__Providers__google__Enabled", "false");
    }
}
